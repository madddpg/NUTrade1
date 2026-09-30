import {
  DocumentData,
  DocumentReference,
  DocumentSnapshot,
  QueryDocumentSnapshot,
  Timestamp,
  Transaction,
} from "firebase-admin/firestore";
import { db } from "./admin";
import { BID_STATUS, LISTING_STATUS } from "./constants";

/** Bids that still count towards the auction — neither the seller nor the bidder has killed them. */
const LIVE_BID_STATUSES: string[] = [BID_STATUS.pending, BID_STATUS.outbid];

export function pesos(centavos: number): string {
  return `₱${(centavos / 100).toFixed(2)}`;
}

/**
 * Awards `listingRef` to `bidSnap` and opens the chat room the two of them trade in.
 *
 * No order is created: the winner's commitment deposit is already held, and the rest of
 * the price is settled in person. The listing goes to `pending_meetup` rather than
 * `matched` — they still have to meet and hand the item over before this is a completed
 * trade, and `markTradeCompleted` is what releases the deposit to the seller.
 *
 * Write-only — callers must have finished every transaction read before calling.
 */
export function awardListing(
  tx: Transaction,
  listingRef: DocumentReference,
  listing: DocumentData,
  bidSnap: DocumentSnapshot | QueryDocumentSnapshot,
  now: Timestamp
): { chatId: string } {
  const bid = bidSnap.data()!;
  const chatRef = db.collection("chats").doc();

  tx.set(chatRef, {
    listingId: listingRef.id,
    listingTitle: listing.title ?? "",
    sellerUid: listing.ownerUid,
    buyerUid: bid.bidderUid,
    participantUids: [listing.ownerUid, bid.bidderUid],
    winningBidCentavos: bid.amountCentavos,
    winningBidId: bidSnap.id,
    lastMessage: "Auction won — arrange your meetup here.",
    lastMessageAt: now,
    status: "active",
    // Both UIDs must land here before the trade closes out; see markTradeCompleted.
    completedBy: [],
    createdAt: now,
  });

  tx.set(chatRef.collection("messages").doc(), {
    senderUid: "system",
    text:
      `Winning bid ${pesos(bid.amountCentavos as number)} on "${listing.title ?? "this item"}". ` +
      `Your deposit is held until the handover. Agree on a campus meetup here, then ` +
      `both of you tap "Mark completed".`,
    type: "system",
    sentAt: now,
  });

  tx.update(bidSnap.ref, {
    status: BID_STATUS.approved,
    chatId: chatRef.id,
    approvedAt: now,
  });
  tx.update(listingRef, {
    status: LISTING_STATUS.pendingMeetup,
    winningBidId: bidSnap.id,
    matchedAt: now,
  });

  return { chatId: chatRef.id };
}

/**
 * Re-derives a listing's denormalized `currentHighestBidCentavos` / `highestBidderUid` /
 * `bidCount` from its surviving bids, and promotes the new top bid back to `pending`.
 *
 * Needed because declining or withdrawing the top bid would otherwise leave the listing
 * advertising a bid nobody stands behind. Read-then-write, so call it before any other
 * write in the transaction.
 */
export async function recomputeTopBid(
  tx: Transaction,
  listingRef: DocumentReference,
  listing: DocumentData,
  now: Timestamp,
  excludeBidId?: string
): Promise<void> {
  const snap = await tx.get(listingRef.collection("bids"));

  const live = snap.docs
    .filter((d) => d.id !== excludeBidId && LIVE_BID_STATUSES.includes(d.data().status))
    .sort((a, b) => (b.data().amountCentavos as number) - (a.data().amountCentavos as number));

  if (live.length === 0) {
    tx.update(listingRef, {
      currentHighestBidCentavos: (listing.startingBidCentavos as number) ?? 0,
      highestBidderUid: null,
      bidCount: 0,
    });
    return;
  }

  const [top, ...rest] = live;
  if (top.data().status !== BID_STATUS.pending) {
    tx.update(top.ref, { status: BID_STATUS.pending, promotedAt: now });
  }
  for (const doc of rest) {
    if (doc.data().status === BID_STATUS.pending) {
      tx.update(doc.ref, { status: BID_STATUS.outbid, outbidAt: now });
    }
  }

  tx.update(listingRef, {
    currentHighestBidCentavos: top.data().amountCentavos,
    highestBidderUid: top.data().bidderUid,
    bidCount: live.length,
  });
}

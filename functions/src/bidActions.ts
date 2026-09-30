import { onCall, HttpsError, CallableRequest } from "firebase-functions/v2/https";
import { Timestamp } from "firebase-admin/firestore";
import { db } from "./admin";
import { BID_STATUS, LISTING_STATUS, REFUND_REASON, REGION } from "./constants";
import { awardListing, recomputeTopBid } from "./matching";
import { readDepositForBid, resolveDeposit } from "./depositResolution";

interface BidActionData {
  listingId: string;
  bidId: string;
}

function readArgs(request: CallableRequest): { uid: string; listingId: string; bidId: string } {
  const auth = request.auth;
  if (!auth) throw new HttpsError("unauthenticated", "Sign in required.");

  const { listingId, bidId } = (request.data ?? {}) as Partial<BidActionData>;
  if (!listingId || !bidId) {
    throw new HttpsError("invalid-argument", "listingId and bidId are required.");
  }
  return { uid: auth.uid, listingId, bidId };
}

/**
 * Seller accepts a bid and ends the auction early. The bid does not have to be the
 * top one — a seller may prefer a slightly lower bid from someone they can actually
 * meet — but it must still be live.
 */
export const approveBid = onCall({ region: REGION }, async (request) => {
  const { uid, listingId, bidId } = readArgs(request);
  const listingRef = db.collection("listings").doc(listingId);
  const bidRef = listingRef.collection("bids").doc(bidId);

  const awarded = await db.runTransaction(async (tx) => {
    const [listingSnap, bidSnap] = await tx.getAll(listingRef, bidRef);
    if (!listingSnap.exists) throw new HttpsError("not-found", "Listing not found.");
    if (!bidSnap.exists) throw new HttpsError("not-found", "Bid not found.");

    const listing = listingSnap.data()!;
    const bid = bidSnap.data()!;

    if (listing.ownerUid !== uid) {
      throw new HttpsError("permission-denied", "Only the seller can approve a bid.");
    }
    if (listing.status !== LISTING_STATUS.active) {
      throw new HttpsError("failed-precondition", "This auction is no longer running.");
    }
    if (bid.status !== BID_STATUS.pending && bid.status !== BID_STATUS.outbid) {
      throw new HttpsError("failed-precondition", "That bid is no longer live.");
    }

    // Read before writing: approving one bid ends the auction for all the others, and
    // their deposits have to be returned here or they stay in escrow forever.
    const others = await tx.get(listingRef.collection("bids"));
    const losing: Array<{ ref: FirebaseFirestore.DocumentReference; data: FirebaseFirestore.DocumentData }> = [];
    for (const doc of others.docs) {
      if (doc.id === bidId) continue;
      if (doc.data().status !== BID_STATUS.pending && doc.data().status !== BID_STATUS.outbid) continue;
      const held = await readDepositForBid(tx, doc.data());
      if (held) losing.push(held);
    }

    const approvedAt = Timestamp.now();
    for (const doc of others.docs) {
      if (doc.id === bidId) continue;
      if (doc.data().status !== BID_STATUS.pending && doc.data().status !== BID_STATUS.outbid) continue;
      tx.update(doc.ref, { status: BID_STATUS.declined, declinedAt: approvedAt });
    }
    for (const held of losing) {
      resolveDeposit(tx, held.ref, held.data, "refunded_to_buyer", { reason: REFUND_REASON.auctionLost });
    }

    return awardListing(tx, listingRef, listing, bidSnap, approvedAt);
  });

  return awarded;
});

/** Seller rejects one bid; the auction carries on with the next-highest promoted back to the top. */
export const declineBid = onCall({ region: REGION }, async (request) => {
  const { uid, listingId, bidId } = readArgs(request);
  const listingRef = db.collection("listings").doc(listingId);
  const bidRef = listingRef.collection("bids").doc(bidId);

  await db.runTransaction(async (tx) => {
    const [listingSnap, bidSnap] = await tx.getAll(listingRef, bidRef);
    if (!listingSnap.exists) throw new HttpsError("not-found", "Listing not found.");
    if (!bidSnap.exists) throw new HttpsError("not-found", "Bid not found.");

    const listing = listingSnap.data()!;
    const bid = bidSnap.data()!;

    if (listing.ownerUid !== uid) {
      throw new HttpsError("permission-denied", "Only the seller can decline a bid.");
    }
    if (bid.status !== BID_STATUS.pending && bid.status !== BID_STATUS.outbid) {
      throw new HttpsError("failed-precondition", "That bid is no longer live.");
    }

    const held = await readDepositForBid(tx, bid);

    const now = Timestamp.now();
    await recomputeTopBid(tx, listingRef, listing, now, bidId);
    tx.update(bidRef, { status: BID_STATUS.declined, declinedAt: now });
    if (held) {
      resolveDeposit(tx, held.ref, held.data, "refunded_to_buyer", {
        reason: REFUND_REASON.sellerCancelled,
        note: "Bid declined — deposit returned",
      });
    }
  });

  return { ok: true };
});

/** Bidder pulls their own bid before the seller acts on it. */
export const withdrawBid = onCall({ region: REGION }, async (request) => {
  const { uid, listingId, bidId } = readArgs(request);
  const listingRef = db.collection("listings").doc(listingId);
  const bidRef = listingRef.collection("bids").doc(bidId);

  await db.runTransaction(async (tx) => {
    const [listingSnap, bidSnap] = await tx.getAll(listingRef, bidRef);
    if (!listingSnap.exists) throw new HttpsError("not-found", "Listing not found.");
    if (!bidSnap.exists) throw new HttpsError("not-found", "Bid not found.");

    const listing = listingSnap.data()!;
    const bid = bidSnap.data()!;

    if (bid.bidderUid !== uid) {
      throw new HttpsError("permission-denied", "You can only withdraw your own bid.");
    }
    if (bid.status !== BID_STATUS.pending && bid.status !== BID_STATUS.outbid) {
      throw new HttpsError("failed-precondition", "That bid can no longer be withdrawn.");
    }
    if (listing.status !== LISTING_STATUS.active) {
      throw new HttpsError("failed-precondition", "This auction has already closed.");
    }

    const held = await readDepositForBid(tx, bid);

    const now = Timestamp.now();
    await recomputeTopBid(tx, listingRef, listing, now, bidId);
    tx.update(bidRef, { status: BID_STATUS.withdrawn, withdrawnAt: now });
    if (held) {
      resolveDeposit(tx, held.ref, held.data, "refunded_to_buyer", {
        reason: "withdrawn",
        note: "Bid withdrawn — deposit returned",
      });
    }
  });

  return { ok: true };
});

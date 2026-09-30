import { onSchedule } from "firebase-functions/v2/scheduler";
import * as logger from "firebase-functions/logger";
import { Timestamp } from "firebase-admin/firestore";
import { db } from "./admin";
import { BID_STATUS, LISTING_KIND, LISTING_STATUS, REFUND_REASON, REGION, listingKind } from "./constants";
import { awardListing } from "./matching";
import { readDepositForBid, resolveDeposit } from "./depositResolution";

/** Bids still in the running when the hammer falls. */
const LIVE_BID_STATUSES = [BID_STATUS.pending, BID_STATUS.outbid];

/**
 * The hammer. Every 5 minutes, sweeps auctions whose `auctionEndsAt` has passed
 * and settles each one: the highest live bid that clears any reserve wins and gets
 * a chat room; anything else (no bids, or a reserve nobody met) goes to `expired`.
 *
 * Deliberately server-side and time-driven — the client's `IDispatcherTimer`
 * countdown is cosmetic, and an app that is closed or asleep must not be able to
 * hold an auction open.
 */
export const closeExpiredAuctions = onSchedule(
  { schedule: "every 5 minutes", region: REGION },
  async () => {
    const now = Timestamp.now();

    const dueSnap = await db
      .collection("listings")
      .where("status", "==", LISTING_STATUS.active)
      .where("auctionEndsAt", "<=", now)
      .limit(200)
      .get();

    if (dueSnap.empty) return;
    logger.info("closeExpiredAuctions: settling auctions", { count: dueSnap.size });

    let matched = 0;
    let expired = 0;

    for (const dueDoc of dueSnap.docs) {
      const listingRef = dueDoc.ref;
      try {
        const outcome = await db.runTransaction(async (tx) => {
          const listingSnap = await tx.get(listingRef);
          if (!listingSnap.exists) return "skipped";
          const listing = listingSnap.data()!;

          // Re-check inside the transaction: a seller may have approved a bid
          // manually between the sweep query and now.
          if (listing.status !== LISTING_STATUS.active) return "skipped";
          // A set-price or swap listing is not an auction, even if something wrote an end time.
          if (listingKind(listing) !== LISTING_KIND.auction) return "skipped";
          const endsAt = listing.auctionEndsAt as Timestamp | undefined;
          if (!endsAt || endsAt.toMillis() > now.toMillis()) return "skipped";

          const bidsSnap = await tx.get(listingRef.collection("bids"));
          const live = bidsSnap.docs
            .filter((d) => LIVE_BID_STATUSES.includes(d.data().status))
            .sort((a, b) => (b.data().amountCentavos as number) - (a.data().amountCentavos as number));

          // Every deposit still held on this auction, read before anything is written —
          // Firestore forbids a read after a write inside a transaction.
          const deposits = new Map<string, { ref: FirebaseFirestore.DocumentReference; data: FirebaseFirestore.DocumentData }>();
          for (const doc of live) {
            const held = await readDepositForBid(tx, doc.data());
            if (held) deposits.set(doc.id, held);
          }

          const settledAt = Timestamp.now();
          const reserve = listing.reservePriceCentavos as number | null | undefined;
          const top = live[0];

          /** Hands a losing bidder their commitment deposit back. */
          const returnDeposit = (bidId: string) => {
            const held = deposits.get(bidId);
            if (held) {
              resolveDeposit(tx, held.ref, held.data, "refunded_to_buyer", {
                reason: REFUND_REASON.auctionLost,
              });
            }
          };

          if (!top) {
            tx.update(listingRef, {
              status: LISTING_STATUS.expired,
              expiredAt: settledAt,
              expiredReason: "no_bids",
            });
            return "expired";
          }

          if (typeof reserve === "number" && (top.data().amountCentavos as number) < reserve) {
            tx.update(listingRef, {
              status: LISTING_STATUS.expired,
              expiredAt: settledAt,
              expiredReason: "reserve_not_met",
            });
            // Nobody met the reserve, so nobody won and every deposit goes back — the
            // top bidder's included.
            for (const doc of live) {
              tx.update(doc.ref, { status: BID_STATUS.declined, declinedAt: settledAt });
              returnDeposit(doc.id);
            }
            return "expired";
          }

          // Losers first, then the winner — awardListing only writes. Without this every
          // losing deposit would sit in escrow forever.
          for (const doc of live.slice(1)) {
            tx.update(doc.ref, { status: BID_STATUS.declined, declinedAt: settledAt });
            returnDeposit(doc.id);
          }
          awardListing(tx, listingRef, listing, top, settledAt);
          return "matched";
        });

        if (outcome === "matched") matched++;
        else if (outcome === "expired") expired++;
      } catch (err) {
        // One bad auction must not abort the sweep; the next run picks it up again.
        logger.error("closeExpiredAuctions: failed to settle listing", {
          listingId: listingRef.id,
          err,
        });
      }
    }

    logger.info("closeExpiredAuctions: done", { matched, expired });
  }
);

import { onSchedule } from "firebase-functions/v2/scheduler";
import * as logger from "firebase-functions/logger";
import { Timestamp } from "firebase-admin/firestore";
import { db } from "./admin";
import { paymongoSecretKey } from "./createQrPayment";
import { reconcilePayment } from "./listingFees";
import { PayMongoClient } from "./paymongo";

/**
 * A PayMongo QR Ph code stops working at its `qrExpiresAt` (PayMongo's own expiry,
 * about 30 minutes after minting), but nothing tells us that on its own — there's no
 * webhook event for it, just a payment that never arrives. Runs every 5 minutes and
 * reverts any listing left stranded in `pending_payment` behind a dead QR back to
 * `draft` so the seller can request a fresh one.
 *
 * Before giving up on a payment it asks PayMongo whether it was in fact paid. A student
 * who paid while the webhook was late or missing gets their listing posted instead of
 * silently reverted; a PayMongo lookup that fails leaves the payment for the next run
 * rather than expiring money that may have arrived.
 */
export const expireStalePayments = onSchedule(
  { schedule: "every 5 minutes", region: "asia-southeast1", secrets: [paymongoSecretKey] },
  async () => {
    const now = Timestamp.now();
    const staleSnap = await db
      .collection("payments")
      .where("status", "==", "awaiting_payment")
      .where("qrExpiresAt", "<=", now)
      .get();

    if (staleSnap.empty) return;

    logger.info("expireStalePayments: checking stale payments", { count: staleSnap.size });
    const client = new PayMongoClient(paymongoSecretKey.value());

    for (const paymentDoc of staleSnap.docs) {
      const payment = paymentDoc.data();

      if (payment.paymongoIntentId) {
        const verdict = await reconcilePayment(client, paymentDoc.ref, payment.paymongoIntentId as string);
        if (verdict !== "unpaid") continue; // settled, or PayMongo unreachable — try again next run
      }

      const listingRef = db.collection("listings").doc(payment.listingId as string);
      await db.runTransaction(async (tx) => {
        const fresh = await tx.get(paymentDoc.ref);
        if (fresh.data()?.status !== "awaiting_payment") return; // settled meanwhile
        const listingSnap = await tx.get(listingRef);
        tx.update(paymentDoc.ref, { status: "expired", expiredAt: now });
        if (listingSnap.exists && listingSnap.data()!.status === "pending_payment") {
          tx.update(listingRef, { status: "draft" });
        }
      });
    }
  }
);

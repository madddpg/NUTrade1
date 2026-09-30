import { onCall, HttpsError, CallableRequest } from "firebase-functions/v2/https";
import { onSchedule } from "firebase-functions/v2/scheduler";
import * as logger from "firebase-functions/logger";
import {
  DocumentData,
  DocumentReference,
  DocumentSnapshot,
  QueryDocumentSnapshot,
  Timestamp,
  Transaction,
} from "firebase-admin/firestore";
import { db } from "./admin";
import { PayMongoClient, mintQrPhPayment, paymentUnavailable } from "./paymongo";
import { paymongoSecretKey } from "./createQrPayment";
import {
  ORDER_PAYMENT_WINDOW_HOURS,
  ORDER_STATUS,
  QR_EXPIRY_SECONDS,
  REGION,
  orderReference,
} from "./constants";

/**
 * An order is what a won auction turns into: the winning bidder owes the seller the bid
 * amount, and has ORDER_PAYMENT_WINDOW_HOURS to pay before the bid is released and the
 * auction reopens.
 *
 * Settlement is PayMongo QR Ph — the same three calls the seller's listing fee uses. That
 * matters for integrity: the money is confirmed by a signed webhook from PayMongo, never
 * by either student asserting it. Neither side can move an order to `paid`, and
 * firestore.rules denies all client writes here, so nobody can take an item without
 * actually having paid for it.
 *
 * The QR itself expires in ~10 minutes while the order lives for 24 hours, so the payment
 * screen can mint a fresh code as often as the buyer needs within that window.
 */

/**
 * Creates the order alongside the chat when an auction is awarded. Write-only, so every
 * transaction read must already be done. Returns the new order id.
 */
export function createOrderForWinningBid(
  tx: Transaction,
  listingRef: DocumentReference,
  listing: DocumentData,
  bidSnap: DocumentSnapshot | QueryDocumentSnapshot,
  chatId: string,
  now: Timestamp
): string {
  const bid = bidSnap.data()!;
  const orderRef = db.collection("orders").doc();

  tx.set(orderRef, {
    listingId: listingRef.id,
    listingTitle: listing.title ?? "",
    listingPhoto: (listing.photos as string[] | undefined)?.[0] ?? null,
    bidId: bidSnap.id,
    chatId,
    sellerUid: listing.ownerUid,
    buyerUid: bid.bidderUid,
    buyerName: bid.bidderName ?? "",
    participantUids: [listing.ownerUid, bid.bidderUid],
    amountCentavos: bid.amountCentavos,
    reference: orderReference(),
    status: ORDER_STATUS.awaitingPayment,
    dueAt: Timestamp.fromMillis(now.toMillis() + ORDER_PAYMENT_WINDOW_HOURS * 3_600_000),
    createdAt: now,
    // Filled in by createOrderQrPayment, refreshed whenever the code expires.
    paymongoIntentId: null,
    qrImageUrl: null,
    qrImageBase64: null,
    qrPayload: null,
    qrExpiresAt: null,
    paidAt: null,
  });

  return orderRef.id;
}

/** Loads an order and checks the caller really is the buyer on it. */
async function loadBuyerOrder(
  request: CallableRequest
): Promise<{ uid: string; ref: DocumentReference; order: DocumentData }> {
  const auth = request.auth;
  if (!auth) throw new HttpsError("unauthenticated", "Sign in required.");

  const { orderId } = (request.data ?? {}) as { orderId?: string };
  if (!orderId) throw new HttpsError("invalid-argument", "orderId is required.");

  const ref = db.collection("orders").doc(orderId);
  const snap = await ref.get();
  if (!snap.exists) throw new HttpsError("not-found", "That order no longer exists.");

  const order = snap.data()!;
  if (order.buyerUid !== auth.uid) {
    throw new HttpsError("permission-denied", "Only the winning bidder can pay for this.");
  }

  if (order.status === ORDER_STATUS.released) {
    throw new HttpsError("failed-precondition", "The payment window closed and the bid was released.");
  }
  if (order.status === ORDER_STATUS.paid) {
    throw new HttpsError("failed-precondition", "This order is already paid.");
  }
  if (order.status === ORDER_STATUS.cancelled) {
    throw new HttpsError("failed-precondition", "This order was cancelled.");
  }

  return { uid: auth.uid, ref, order };
}

/**
 * Mints a QR Ph code the winning bidder scans to pay the seller.
 *
 * Returning a code says nothing about payment: only paymongoWebhook moves the order to
 * `paid`. Safe to call repeatedly — a QR lapses after about ten minutes while the order
 * stands for a day, so the payment screen re-mints on demand. A still-valid code is
 * handed back rather than stacking another intent against the same order.
 */
export const createOrderQrPayment = onCall(
  { secrets: [paymongoSecretKey], region: REGION },
  async (request) => {
    const { ref, order } = await loadBuyerOrder(request);
    const now = Timestamp.now();

    const existingExpiry = order.qrExpiresAt as Timestamp | null | undefined;
    if (existingExpiry && existingExpiry.toMillis() > now.toMillis() + 30_000) {
      return {
        reused: true,
        qrImageUrl: order.qrImageUrl ?? null,
        qrImageBase64: order.qrImageBase64 ?? null,
        qrPayload: order.qrPayload ?? null,
        amountCentavos: order.amountCentavos,
        expiresAt: existingExpiry.toMillis(),
        reference: order.reference,
      };
    }

    const buyerSnap = await db.collection("users").doc(order.buyerUid as string).get();
    const buyer = buyerSnap.data() ?? {};

    const client = new PayMongoClient(paymongoSecretKey.value());
    let minted;
    try {
      minted = await mintQrPhPayment(client, {
        amountCentavos: order.amountCentavos as number,
        description: `NUTrade winning bid — ${order.listingTitle} (${order.reference})`,
        billingName: (buyer.displayName as string) ?? "NUTrade student",
        billingEmail: (buyer.email as string) ?? "",
      });
    } catch (err) {
      throw paymentUnavailable(err);
    }
    const { intentId, qr } = minted;

    const expiresAt = Timestamp.fromMillis(now.toMillis() + QR_EXPIRY_SECONDS * 1000);
    await ref.update({
      paymongoIntentId: intentId,
      qrImageUrl: qr.qrImageUrl ?? null,
      qrImageBase64: qr.qrImageBase64 ?? null,
      qrPayload: qr.qrPayload ?? null,
      qrExpiresAt: expiresAt,
    });

    logger.info("createOrderQrPayment: code issued", { orderId: ref.id, intentId });

    return {
      reused: false,
      qrImageUrl: qr.qrImageUrl ?? null,
      qrImageBase64: qr.qrImageBase64 ?? null,
      qrPayload: qr.qrPayload ?? null,
      amountCentavos: order.amountCentavos,
      expiresAt: expiresAt.toMillis(),
      reference: order.reference,
    };
  }
);

/**
 * Settles a paid order from the PayMongo webhook, matched on the payment intent the QR
 * was minted against. Returns false when nothing matched so the webhook can fall through
 * to its other handlers instead of treating it as an error.
 */
export async function settleOrderPaid(intentId: string | undefined): Promise<boolean> {
  if (!intentId) return false;

  const found = await db
    .collection("orders")
    .where("paymongoIntentId", "==", intentId)
    .limit(1)
    .get();
  if (found.empty) return false;

  const snap = found.docs[0];
  const order = snap.data();
  if (order.status === ORDER_STATUS.paid) return true; // PayMongo retried the same event.

  const now = Timestamp.now();
  await snap.ref.update({ status: ORDER_STATUS.paid, paidAt: now });

  await db.collection("chats").doc(order.chatId as string).collection("messages").doc().set({
    senderUid: "system",
    text:
      `Payment of ₱${((order.amountCentavos as number) / 100).toFixed(2)} received ` +
      `(${order.reference}). Arrange the handover, then both tap Mark completed.`,
    type: "system",
    sentAt: now,
  });

  logger.info("settleOrderPaid: order paid", { orderId: snap.id, intentId });
  return true;
}

/**
 * Releases bids whose payment window closed unpaid. The auction goes back to `active`
 * with the bid struck out, so the seller can approve someone else rather than being stuck
 * behind a buyer who never paid.
 */
export const releaseUnpaidOrders = onSchedule(
  { schedule: "every 5 minutes", region: REGION },
  async () => {
    const now = Timestamp.now();
    const due = await db
      .collection("orders")
      .where("status", "==", ORDER_STATUS.awaitingPayment)
      .where("dueAt", "<=", now)
      .limit(100)
      .get();

    if (due.empty) return;
    logger.info("releaseUnpaidOrders: releasing", { count: due.size });

    for (const orderDoc of due.docs) {
      const order = orderDoc.data();
      try {
        await db.runTransaction(async (tx) => {
          const fresh = await tx.get(orderDoc.ref);
          // Re-check inside the transaction: the webhook may have landed meanwhile.
          if (!fresh.exists || fresh.data()!.status !== ORDER_STATUS.awaitingPayment) return;

          const listingRef = db.collection("listings").doc(order.listingId as string);
          const listingSnap = await tx.get(listingRef);

          tx.update(orderDoc.ref, { status: ORDER_STATUS.released, releasedAt: now });
          tx.update(listingRef.collection("bids").doc(order.bidId as string), {
            status: "released",
            releasedAt: now,
          });

          // Only reopen a listing still sitting on this order's sale.
          if (listingSnap.exists && listingSnap.data()!.status === "matched") {
            tx.update(listingRef, {
              status: "active",
              winningBidId: null,
              orderId: null,
              matchedAt: null,
              reopenedAt: now,
            });
          }

          tx.set(db.collection("chats").doc(order.chatId as string).collection("messages").doc(), {
            senderUid: "system",
            text: "Payment window closed with no payment, so the bid was released and the auction reopened.",
            type: "system",
            sentAt: now,
          });
        });
      } catch (err) {
        logger.error("releaseUnpaidOrders: failed", { orderId: orderDoc.id, err });
      }
    }
  }
);

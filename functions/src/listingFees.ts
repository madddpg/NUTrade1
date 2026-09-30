import * as logger from "firebase-functions/logger";
import { DocumentReference, FieldValue, Timestamp } from "firebase-admin/firestore";
import { db } from "./admin";
import { LISTING_STATUS, ListingPackage } from "./constants";
import { pendingApprovalFields } from "./listingApproval";
import { notifyUser } from "./notify";
import { PayMongoClient, intentIsPaid } from "./paymongo";

/**
 * Settling a listing fee, from whichever side learns about it first.
 *
 * PayMongo's webhook is the fast path, but it is not the only one: a webhook that is
 * misconfigured, delayed or dropped used to leave a student who had paid staring at the
 * QR code while the expiry sweep quietly reverted their listing to draft. So the payment
 * screen, createQrPayment and expireStalePayments also ask PayMongo directly
 * (reconcileListingFees). Either way the evidence is PayMongo's, fetched with our secret
 * key — nothing a student says can settle a fee.
 */

/** Payment docs that may still turn out to have been paid. */
const UNSETTLED_PAYMENT_STATUSES = ["awaiting_payment", "expired"];

/**
 * Marks one listing-fee payment paid, posts its listing into the admin queue, and
 * records the revenue. Idempotent and race-safe: the webhook and every reconciler may
 * call it for the same payment, and only the first call writes anything.
 *
 * Returns true when this call is the one that settled it.
 */
export async function settleListingFee(
  paymentRef: DocumentReference,
  source: "webhook" | "reconcile"
): Promise<boolean> {
  const outcome = await db.runTransaction(async (tx) => {
    const paymentSnap = await tx.get(paymentRef);
    const payment = paymentSnap.data();
    if (!payment || payment.status === "paid") return null;

    const listingRef = db.collection("listings").doc(payment.listingId as string);
    const listingSnap = await tx.get(listingRef);
    if (!listingSnap.exists) return null;
    const ownerSnap = await tx.get(db.collection("users").doc(payment.uid as string));

    const now = Timestamp.now();
    tx.update(paymentRef, { status: "paid", paidAt: now, settledBy: source });

    // Paying the fee posts the listing into the admin queue; it goes live only when an
    // admin approves it (approveListing). A payment that clears after the QR sweep
    // already reverted the listing to draft still counts — the money arrived. Anything
    // further along (a second payment for the same listing) is recorded but never moves
    // the listing backwards.
    // A listing already in the queue with no paidPackage was put there by the web admin
    // panel's paymongoWebhook, which moves the status and nothing else — this payment is
    // what it saw, so it still gets stamped here.
    const listing = listingSnap.data()!;
    const status = listing.status;
    const submitted = status === LISTING_STATUS.pendingPayment || status === LISTING_STATUS.draft
      || (status === LISTING_STATUS.pendingApproval && !listing.paidPackage);
    if (submitted) {
      tx.update(listingRef, pendingApprovalFields(payment.package as ListingPackage, now));
    } else {
      logger.warn("settleListingFee: fee paid for a listing already past payment", {
        listingId: payment.listingId,
        status,
      });
    }

    // In the web admin panel's shape, which lists these as its revenue ledger: `amount`
    // in pesos and `timestamp`, the field names its own paymongoWebhook writes.
    tx.set(db.collection("transactions").doc(), {
      paymentId: paymentRef.id,
      payMongoIntentId: payment.paymongoIntentId,
      userId: payment.uid,
      userEmail: (ownerSnap.data()?.email as string | undefined) ?? "",
      listingId: payment.listingId,
      packageType: payment.package,
      amount: (payment.amount as number) / 100,
      amountCentavos: payment.amount,
      status: "paid",
      timestamp: now,
    });

    tx.set(
      db.collection("counters").doc("revenue"),
      {
        grossCentavos: FieldValue.increment(payment.amount as number),
        transactionCount: FieldValue.increment(1),
        updatedAt: now,
      },
      { merge: true }
    );

    return { uid: payment.uid as string, listingId: payment.listingId as string, submitted };
  });

  if (!outcome) return false;
  logger.info("settleListingFee: settled", { paymentId: paymentRef.id, source, ...outcome });

  if (outcome.submitted) {
    await notifyUser(outcome.uid, {
      title: "Item successfully posted",
      body: "Payment confirmed — an admin will review your listing before it goes live.",
      data: { listingId: outcome.listingId, type: "listing_submitted" },
    });
  }
  return true;
}

export type PaymentVerdict = "paid" | "unpaid" | "unknown";

/**
 * PayMongo's answer, fetched with our secret key, to "was this intent paid?". "unknown"
 * means the lookup itself failed, so the caller should try again rather than decide.
 */
export async function checkIntentWithPayMongo(client: PayMongoClient, intentId: string): Promise<PaymentVerdict> {
  try {
    return intentIsPaid(await client.retrievePaymentIntent(intentId)) ? "paid" : "unpaid";
  } catch (err) {
    logger.warn("checkIntentWithPayMongo: PayMongo lookup failed", { intentId, err });
    return "unknown";
  }
}

/**
 * Asks PayMongo about one payment doc and settles it if PayMongo says it was paid.
 * A failed lookup comes back "unknown", so the caller can retry later.
 */
export async function reconcilePayment(
  client: PayMongoClient,
  paymentRef: DocumentReference,
  intentId: string,
  source: "webhook" | "reconcile" = "reconcile"
): Promise<PaymentVerdict> {
  const verdict = await checkIntentWithPayMongo(client, intentId);
  if (verdict === "paid") await settleListingFee(paymentRef, source);
  return verdict;
}

/**
 * Checks every not-yet-settled fee payment for a listing with PayMongo, settling the
 * first one that was actually paid. "paid" once one is settled; "unknown" if PayMongo
 * couldn't be asked about at least one of them; otherwise "unpaid".
 */
export async function verifyListingFees(client: PayMongoClient, listingId: string): Promise<PaymentVerdict> {
  const payments = await db.collection("payments").where("listingId", "==", listingId).get();

  let verdict: PaymentVerdict = "unpaid";
  for (const doc of payments.docs) {
    const payment = doc.data();
    if (!UNSETTLED_PAYMENT_STATUSES.includes(payment.status) || !payment.paymongoIntentId) continue;
    const result = await reconcilePayment(client, doc.ref, payment.paymongoIntentId as string);
    if (result === "paid") return "paid";
    if (result === "unknown") verdict = "unknown";
  }
  return verdict;
}

/** True if the listing's fee is now settled. See {@link verifyListingFees}. */
export async function reconcileListingFees(client: PayMongoClient, listingId: string): Promise<boolean> {
  return (await verifyListingFees(client, listingId)) === "paid";
}

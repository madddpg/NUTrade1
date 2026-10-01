import * as logger from "firebase-functions/logger";
import { DocumentData, DocumentReference, FieldValue, Timestamp, Transaction } from "firebase-admin/firestore";
import { db } from "./admin";
import { PayMongoClient, paymentIdFromIntent } from "./paymongo";
import { LEDGER_KIND, postToLedger } from "./wallet";
import { DEPOSIT_STATUS, REFUND_REASON } from "./constants";

/**
 * The one place a bid deposit changes state.
 *
 * Every path funnels through {@link resolveDeposit} so a deposit can only ever end in one
 * of three places, each of them written together with its ledger row in a single
 * transaction:
 *
 *   showed up, outbid, lost, or the seller cancelled -> back to the bidder as bid credit
 *   winning bidder no-show -> the seller's bid credit, plus a strike on the bidder
 *
 * Forfeit is the only outcome that takes the bond away from the bidder. It does not
 * become platform revenue: the seller who was stood up receives it as bid credit.
 *
 * **Why returns are bid credit.** QR Ph captures immediately, so there is no
 * authorisation hold to release — the money really did leave the student's GCash. A
 * PayMongo refund takes days, costs a fee, and is impossible outright for QR Ph paid
 * through Maya. Credit is instant and pays the next deposit. It cannot be cashed out.
 */

export type DepositOutcome = "credited_to_seller" | "refunded_to_buyer" | "forfeited";

export interface ResolveOptions {
  /** Why, for the student-facing note and for disputes. One of REFUND_REASON, or free text. */
  reason?: string;
  /** How the money got back, when it was returned. */
  via?: "wallet_credit" | "paymongo_refund";
  note?: string;
}

/** Deposits that have been paid and not yet resolved. Only these can move. */
export function isHeld(deposit: DocumentData): boolean {
  return deposit.status === DEPOSIT_STATUS.lockedInEscrow;
}

/**
 * Resolves one held deposit inside the caller's transaction. Returns false — writing
 * nothing — when the deposit is not held, which is what makes every caller idempotent.
 *
 * Write-only apart from the treasury counter, so callers must have finished all of their
 * transaction reads first.
 */
export function resolveDeposit(
  tx: Transaction,
  intentRef: DocumentReference,
  deposit: DocumentData,
  outcome: DepositOutcome,
  options: ResolveOptions = {}
): boolean {
  if (!isHeld(deposit)) return false;

  const amount = deposit.depositCentavos as number;
  const now = Timestamp.now();
  const context = {
    listingId: (deposit.listingId as string) ?? null,
    bidId: (deposit.committedBidId as string) ?? null,
  };

  if (outcome === "credited_to_seller") {
    tx.update(intentRef, {
      status: DEPOSIT_STATUS.creditedToSeller,
      resolvedAt: now,
      refundReason: null,
    });
    postToLedger(tx, deposit.sellerUid as string, amount, LEDGER_KIND.depositCredit, {
      ...context,
      note: options.note ?? `Handover confirmed — ${deposit.listingTitle ?? "trade"}`,
    });
    return true;
  }

  if (outcome === "refunded_to_buyer") {
    const via = options.via ?? "wallet_credit";
    tx.update(intentRef, {
      status: DEPOSIT_STATUS.refundedToBuyer,
      resolvedAt: now,
      refundReason: options.reason ?? null,
      refundedVia: via,
    });

    // A PayMongo refund puts real money back at the source, so there is nothing to credit
    // — crediting as well would pay the student twice.
    if (via === "wallet_credit") {
      postToLedger(tx, deposit.bidderUid as string, amount, LEDGER_KIND.refundCredit, {
        ...context,
        note: options.note ?? refundNote(options.reason),
      });
    }
    return true;
  }

  // Forfeited. The bidder loses the bond and the seller can spend it on a later bid.
  tx.update(intentRef, {
    status: DEPOSIT_STATUS.forfeited,
    resolvedAt: now,
    refundReason: options.reason ?? null,
  });
  const sellerUid = deposit.sellerUid as string | undefined;
  if (sellerUid) {
    postToLedger(tx, sellerUid, amount, LEDGER_KIND.forfeitCredit, {
      ...context,
      note: options.note ?? "Bidder didn't show — their deposit is yours to bid with",
    });
  }
  tx.set(
    db.collection("counters").doc("revenue"),
    {
      forfeitCreditCentavos: FieldValue.increment(amount),
      updatedAt: now,
    },
    { merge: true }
  );
  return true;
}

function refundNote(reason?: string): string {
  switch (reason) {
    case REFUND_REASON.outbid:
      return "Outbid — deposit returned";
    case REFUND_REASON.auctionLost:
      return "Auction lost — deposit returned";
    case REFUND_REASON.sellerCancelled:
      return "Seller cancelled — deposit returned";
    default:
      return "Deposit returned";
  }
}

/**
 * Tries to put a deposit back on the card or wallet it came from.
 *
 * Used only for seller cancellation, where the buyer gets nothing out of the trade and
 * real money back is the fair outcome. Returns false on any failure — including the Maya
 * case, which cannot be refunded at all — and the caller then credits the wallet instead.
 * Never throws: a failed refund must not block the resolution.
 */
export async function tryPayMongoRefund(
  client: PayMongoClient,
  deposit: DocumentData
): Promise<boolean> {
  const intentId = deposit.paymongoIntentId as string | undefined;
  if (!intentId) return false;

  try {
    const intent = await client.retrievePaymentIntent(intentId);
    const paymentId = paymentIdFromIntent(intent);
    if (!paymentId) {
      logger.warn("tryPayMongoRefund: no captured payment on the intent", { intentId });
      return false;
    }

    await client.createRefund(paymentId, deposit.depositCentavos as number, "requested_by_customer");
    logger.info("tryPayMongoRefund: refunded", { intentId, paymentId });
    return true;
  } catch (err) {
    // Expected for QR Ph paid through Maya, which PayMongo refuses to refund. Not an
    // error worth failing the cancellation over — the wallet credit is the fallback.
    logger.warn("tryPayMongoRefund: refund refused, falling back to wallet credit", { intentId, err });
    return false;
  }
}

/** Reads a bid's deposit, or null. Call during the read phase of a transaction. */
export async function readDepositForBid(
  tx: Transaction,
  bid: DocumentData
): Promise<{ ref: DocumentReference; data: DocumentData } | null> {
  const depositId = bid["depositIntentId"] as string | undefined;
  if (!depositId) return null;

  const ref = db.collection("bidIntents").doc(depositId);
  const snap = await tx.get(ref);
  return snap.exists ? { ref, data: snap.data()! } : null;
}

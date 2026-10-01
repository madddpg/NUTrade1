import { onCall, HttpsError } from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import { Timestamp } from "firebase-admin/firestore";
import { db } from "./admin";
import { LEDGER_KIND, postToLedger } from "./wallet";
import { PAYOUT_STATUS, REGION } from "./constants";

/**
 * Cash-out is no longer offered. Bid credit pays the next deposit.
 *
 * `requestPayout` refuses. `declinePayout` remains so an admin can return any request
 * that was already waiting. `markPayoutPaid` remains only for a request an admin has
 * already sent by hand.
 */

function payoutRef(id: string) {
  return db.collection("payoutRequests").doc(id);
}

function assertAdmin(role: unknown): void {
  if (role !== "admin") throw new HttpsError("permission-denied", "Admins only.");
}

/** Refuses a new cash-out. Bid credit is spent on the next deposit instead. */
export const requestPayout = onCall({ region: REGION }, async (request) => {
  if (!request.auth) throw new HttpsError("unauthenticated", "Sign in required.");
  throw new HttpsError(
    "failed-precondition",
    "Bid credit can't be cashed out. It applies automatically to your next deposit."
  );
});

/** Admin confirms they have sent the money. The balance was already debited. */
export const markPayoutPaid = onCall({ region: REGION }, async (request) => {
  const auth = request.auth;
  if (!auth) throw new HttpsError("unauthenticated", "Sign in required.");
  assertAdmin(auth.token.role);

  const id = typeof request.data?.payoutRequestId === "string" ? request.data.payoutRequestId : "";
  if (!id) throw new HttpsError("invalid-argument", "payoutRequestId is required.");

  await db.runTransaction(async (tx) => {
    const snapshot = await tx.get(payoutRef(id));
    const payout = snapshot.data();
    if (!payout) throw new HttpsError("not-found", "No such payout request.");
    if (payout.status !== PAYOUT_STATUS.requested) {
      throw new HttpsError("failed-precondition", "That request has already been resolved.");
    }

    tx.update(payoutRef(id), {
      status: PAYOUT_STATUS.paid,
      resolvedAt: Timestamp.now(),
      resolvedBy: auth.uid,
    });
  });

  logger.info("markPayoutPaid", { requestId: id, admin: auth.uid });
  return { payoutRequestId: id, status: PAYOUT_STATUS.paid };
});

/** Admin refuses. The held balance goes back to the student, with a ledger row saying so. */
export const declinePayout = onCall({ region: REGION }, async (request) => {
  const auth = request.auth;
  if (!auth) throw new HttpsError("unauthenticated", "Sign in required.");
  assertAdmin(auth.token.role);

  const data = request.data ?? {};
  const id = typeof data.payoutRequestId === "string" ? data.payoutRequestId : "";
  if (!id) throw new HttpsError("invalid-argument", "payoutRequestId is required.");
  const reason = typeof data.reason === "string" ? data.reason.trim().slice(0, 200) : "";

  const refunded = await db.runTransaction(async (tx) => {
    const snapshot = await tx.get(payoutRef(id));
    const payout = snapshot.data();
    if (!payout) throw new HttpsError("not-found", "No such payout request.");
    if (payout.status !== PAYOUT_STATUS.requested) {
      throw new HttpsError("failed-precondition", "That request has already been resolved.");
    }

    const amount = payout.amountCentavos as number;
    tx.update(payoutRef(id), {
      status: PAYOUT_STATUS.declined,
      resolvedAt: Timestamp.now(),
      resolvedBy: auth.uid,
      declineReason: reason || null,
    });

    postToLedger(tx, payout.uid as string, amount, LEDGER_KIND.refundCredit, {
      payoutRequestId: id,
      note: reason ? `Payout declined: ${reason}` : "Payout declined",
    });

    return { uid: payout.uid as string, amount };
  });

  logger.info("declinePayout", { requestId: id, admin: auth.uid, ...refunded });
  return { payoutRequestId: id, status: PAYOUT_STATUS.declined };
});

import { onCall, HttpsError } from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import { Timestamp } from "firebase-admin/firestore";
import { db } from "./admin";
import { LEDGER_KIND, postToLedger, readBalance } from "./wallet";
import {
  MIN_PAYOUT_CENTAVOS,
  PAYOUT_METHODS,
  PAYOUT_STATUS,
  PayoutMethod,
  REGION,
} from "./constants";

/**
 * Cashing an internal balance out to GCash or Maya.
 *
 * NUTrade does not move the money itself — an admin does, by hand, and then records it
 * here. So the balance is debited the moment the request is made, not when the admin
 * pays: a student who could ask twice while the first request sat in the queue could be
 * paid twice. Declining a request puts the balance back.
 *
 * That makes `payoutRequests` the held-funds record, and the wallet balance always means
 * "spendable now" rather than "earned so far".
 */

const MAX_ACCOUNT_NAME = 60;

function payoutRef(id: string) {
  return db.collection("payoutRequests").doc(id);
}

function cleanAccountName(raw: unknown): string {
  const name = typeof raw === "string" ? raw.trim().replace(/\s+/g, " ") : "";
  if (name.length === 0) {
    throw new HttpsError("invalid-argument", "Enter the name on the account.");
  }
  if (name.length > MAX_ACCOUNT_NAME) {
    throw new HttpsError("invalid-argument", `Keep the account name under ${MAX_ACCOUNT_NAME} characters.`);
  }
  return name;
}

/** Philippine mobile numbers, the only thing GCash and Maya accept. */
function cleanAccountNumber(raw: unknown): string {
  const digits = (typeof raw === "string" ? raw : "").replace(/[^\d]/g, "");
  const local = digits.startsWith("63") ? `0${digits.slice(2)}` : digits;
  if (!/^09\d{9}$/.test(local)) {
    throw new HttpsError("invalid-argument", "Enter the 11-digit mobile number, starting 09.");
  }
  return local;
}

function cleanMethod(raw: unknown): PayoutMethod {
  const method = typeof raw === "string" ? raw.trim().toLowerCase() : "";
  if (!(PAYOUT_METHODS as readonly string[]).includes(method)) {
    throw new HttpsError("invalid-argument", "Choose GCash or Maya.");
  }
  return method as PayoutMethod;
}

function assertAdmin(role: unknown): void {
  if (role !== "admin") throw new HttpsError("permission-denied", "Admins only.");
}

/**
 * Student asks for their balance. Debits it in the same transaction that records the
 * request, so the money is held rather than merely promised.
 */
export const requestPayout = onCall({ region: REGION }, async (request) => {
  const auth = request.auth;
  if (!auth) throw new HttpsError("unauthenticated", "Sign in required.");

  const data = request.data ?? {};
  const method = cleanMethod(data.method);
  const accountName = cleanAccountName(data.accountName);
  const accountNumber = cleanAccountNumber(data.accountNumber);

  const requestRef = db.collection("payoutRequests").doc();

  const amount = await db.runTransaction(async (tx) => {
    // Read before any write, and read inside the transaction: two taps in quick
    // succession would otherwise both see the same balance and both be allowed.
    const balance = await readBalance(auth.uid, tx);
    if (balance < MIN_PAYOUT_CENTAVOS) {
      return null;
    }

    const now = Timestamp.now();
    tx.set(requestRef, {
      uid: auth.uid,
      amountCentavos: balance,
      method,
      accountName,
      accountNumber,
      status: PAYOUT_STATUS.requested,
      createdAt: now,
      resolvedAt: null,
      resolvedBy: null,
      declineReason: null,
    });

    // The whole balance goes out at once — a partial payout is more states to reconcile
    // by hand than it is worth while an admin is sending these one by one.
    postToLedger(tx, auth.uid, -balance, LEDGER_KIND.payout, {
      payoutRequestId: requestRef.id,
      note: `Payout to ${method}`,
    });

    return balance;
  });

  if (amount === null) {
    throw new HttpsError(
      "failed-precondition",
      `You need at least ₱${(MIN_PAYOUT_CENTAVOS / 100).toFixed(2)} before you can cash out.`
    );
  }

  logger.info("requestPayout: raised", { uid: auth.uid, requestId: requestRef.id, amount });
  return { payoutRequestId: requestRef.id, amountCentavos: amount };
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

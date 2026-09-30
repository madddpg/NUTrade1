import { FieldValue, Timestamp, Transaction } from "firebase-admin/firestore";
import { db } from "./admin";

/**
 * The student's internal balance, and the append-only record behind it.
 *
 * Two documents move together or not at all: `wallets/{uid}` holds the running total, and
 * every change writes a `ledgerEntries` row saying why. The total is therefore always
 * reconstructible by summing the entries — which is the only reason to trust it, since a
 * balance nobody can audit is just a number.
 *
 * Nothing here is a callable. Balances change as a *consequence* of something else
 * settling — a handover confirmed, a deposit forfeited, a payout marked paid — so every
 * mutation takes the caller's transaction and rides along with whatever else that
 * transaction is writing. There is no "add money" endpoint by design.
 */

/** What a ledger entry is for. Stored as-is on the row, and shown to the student. */
export const LEDGER_KIND = {
  /** A buyer's commitment deposit credited to the seller after a confirmed handover. */
  depositCredit: "deposit_credit",
  /** A seller cancelled after a deposit was paid; the buyer gets in-app credit. */
  refundCredit: "refund_credit",
  /** Balance paid out to the student off-platform. Always negative. */
  payout: "payout",
} as const;

export type LedgerKind = (typeof LEDGER_KIND)[keyof typeof LEDGER_KIND];

export interface LedgerContext {
  listingId?: string | null;
  bidId?: string | null;
  payoutRequestId?: string | null;
  note?: string | null;
}

export function walletRef(uid: string) {
  return db.collection("wallets").doc(uid);
}

/**
 * Moves `amountCentavos` (negative to debit) and records why, inside the caller's
 * transaction.
 *
 * Firestore requires every read in a transaction to happen before any write, so this
 * deliberately does **not** read the wallet: `FieldValue.increment` is applied blind and
 * resolved server-side, which is both correct under concurrency and safe to call after
 * the caller has already written. A caller that must refuse to go negative has to read
 * the balance itself, first — see `requestPayout`.
 */
export function postToLedger(
  tx: Transaction,
  uid: string,
  amountCentavos: number,
  kind: LedgerKind,
  context: LedgerContext = {}
): void {
  const now = Timestamp.now();

  tx.set(
    walletRef(uid),
    {
      uid,
      balanceCentavos: FieldValue.increment(amountCentavos),
      updatedAt: now,
    },
    { merge: true }
  );

  tx.set(db.collection("ledgerEntries").doc(), {
    uid,
    kind,
    amountCentavos,
    listingId: context.listingId ?? null,
    bidId: context.bidId ?? null,
    payoutRequestId: context.payoutRequestId ?? null,
    note: context.note ?? null,
    createdAt: now,
  });
}

/** Current balance in centavos. Zero for a student who has never had a wallet document. */
export async function readBalance(uid: string, tx?: Transaction): Promise<number> {
  const ref = walletRef(uid);
  const snapshot = await (tx ? tx.get(ref) : ref.get());
  return (snapshot.data()?.["balanceCentavos"] as number | undefined) ?? 0;
}

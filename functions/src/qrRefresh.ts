import * as logger from "firebase-functions/logger";
import { DocumentReference, Timestamp } from "firebase-admin/firestore";
import { db } from "./admin";
import { QR_EXPIRY_SECONDS } from "./constants";
import { PayMongoClient, intentNeedsFreshQr, issueReplacementQr, replacementBlocked } from "./paymongo";

/** Shown on the QR screen after a wallet rejects a scan. */
export const FRESH_QR_NOTE =
  "The last scan didn't go through. This is a new code — scan this one.";

export interface StoredQr {
  qrImageUrl: string | null;
  qrImageBase64: string | null;
  qrPayload: string | null;
  expiresAt: number | null;
  replaced: boolean;
  testMode: boolean;
}

function storedFrom(data: FirebaseFirestore.DocumentData): StoredQr {
  const expires = data.qrExpiresAt as Timestamp | undefined;
  return {
    qrImageUrl: (data.qrImageUrl as string | undefined) ?? null,
    qrImageBase64: (data.qrImageBase64 as string | undefined) ?? null,
    qrPayload: (data.qrPayload as string | undefined) ?? null,
    expiresAt: typeof expires?.toMillis === "function" ? expires.toMillis() : null,
    replaced: false,
    testMode: data.paymongoTestMode === true,
  };
}

function expiryTimestamp(iso: string | undefined): Timestamp {
  const parsed = iso ? Date.parse(iso) : NaN;
  return Timestamp.fromMillis(
    Number.isNaN(parsed) ? Date.now() + QR_EXPIRY_SECONDS * 1000 : parsed
  );
}

async function billingFor(uid: string): Promise<{ name: string; email: string }> {
  const snap = await db.collection("users").doc(uid).get();
  const profile = snap.data() ?? {};
  return {
    name: (profile.displayName as string) || "NUTrade student",
    email: (profile.email as string) || "",
  };
}

/**
 * When PayMongo says the last QR Ph attempt failed, attaches a new code and
 * writes it onto the same document. A live code, a paid intent, or a replacement
 * issued in the last few seconds is left alone.
 *
 * One writer at a time: a poll and the webhook can both notice the failure.
 */
export async function refreshQrOnDoc(
  client: PayMongoClient,
  ref: DocumentReference,
  data: FirebaseFirestore.DocumentData,
  intentId: string | null | undefined,
  uid: string | null | undefined
): Promise<StoredQr> {
  const latest = (await ref.get()).data() ?? data;
  if (!intentId) return storedFrom(latest);

  const replacedAt = latest.qrReplacedAt as Timestamp | undefined;
  const generation = (latest.qrGeneration as number) ?? 0;
  const replacedAtMillis = typeof replacedAt?.toMillis === "function" ? replacedAt.toMillis() : null;
  if (replacementBlocked(replacedAtMillis, generation)) return storedFrom(latest);

  let current;
  try {
    current = await client.retrievePaymentIntent(intentId);
  } catch (err) {
    logger.warn("refreshQrOnDoc: could not read the payment intent", { intentId, err });
    return storedFrom(latest);
  }
  if (!intentNeedsFreshQr(current)) return storedFrom(latest);

  const claimed = await db.runTransaction(async (tx) => {
    const snap = await tx.get(ref);
    const fresh = snap.data();
    if (!fresh) return null;

    const replacingAt = fresh.qrReplacingAt as Timestamp | undefined;
    const claimIsFresh =
      fresh.qrReplacing === true &&
      typeof replacingAt?.toMillis === "function" &&
      Date.now() - replacingAt.toMillis() < 60_000;
    if (claimIsFresh) return null;

    const againAt = fresh.qrReplacedAt as Timestamp | undefined;
    const againGeneration = (fresh.qrGeneration as number) ?? 0;
    const againMillis = typeof againAt?.toMillis === "function" ? againAt.toMillis() : null;
    if (replacementBlocked(againMillis, againGeneration)) return null;

    tx.update(ref, { qrReplacing: true, qrReplacingAt: Timestamp.now() });
    return { generation: againGeneration, status: fresh.status as string | undefined, testMode: fresh.paymongoTestMode === true };
  });

  if (!claimed) return storedFrom((await ref.get()).data() ?? latest);

  const billing = uid ? await billingFor(uid) : { name: "NUTrade student", email: "" };
  try {
    const fresh = await issueReplacementQr(client, intentId, billing.name, billing.email);
    if (!fresh.replaced) {
      await ref.update({ qrReplacing: false });
      return storedFrom((await ref.get()).data() ?? latest);
    }

    const expires = expiryTimestamp(fresh.qr.expiresAt);
    const update: Record<string, unknown> = {
      qrImageUrl: fresh.qr.qrImageUrl ?? null,
      qrImageBase64: fresh.qr.qrImageBase64 ?? null,
      qrPayload: fresh.qr.qrPayload ?? null,
      qrExpiresAt: expires,
      qrReplacedAt: Timestamp.now(),
      qrGeneration: claimed.generation + 1,
      qrNote: FRESH_QR_NOTE,
      qrReplacing: false,
    };
    // An older webhook stamped `failed` on one declined scan and then ignored
    // the real payment. Opening it again lets a later scan settle.
    if (claimed.status === "failed") update.status = "awaiting_payment";
    await ref.update(update);

    return {
      qrImageUrl: fresh.qr.qrImageUrl ?? null,
      qrImageBase64: fresh.qr.qrImageBase64 ?? null,
      qrPayload: fresh.qr.qrPayload ?? null,
      expiresAt: expires.toMillis(),
      replaced: true,
      testMode: claimed.testMode,
    };
  } catch (err) {
    logger.error("refreshQrOnDoc: could not replace the QR", { intentId, err });
    await ref.update({ qrReplacing: false }).catch(() => undefined);
    return storedFrom(latest);
  }
}

import * as crypto from "crypto";
import { onCall, HttpsError } from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import { getAuth } from "firebase-admin/auth";
import { Timestamp } from "firebase-admin/firestore";
import { db } from "./admin";
import { mailSecrets, sendMail } from "./brevo";
import { consumeMailAllowance } from "./mailRateLimit";
import { otpEmail } from "./mailTemplates";
import {
  OTP_MAX_ATTEMPTS,
  OTP_RESEND_COOLDOWN_SECONDS,
  OTP_TTL_MINUTES,
  REGION,
} from "./constants";

/** Six digits, uniformly distributed. `randomInt` is rejection-sampled, unlike `random() * n`. */
export function generateCode(): string {
  return String(crypto.randomInt(0, 1_000_000)).padStart(6, "0");
}

/**
 * Codes are stored hashed, never in plaintext. A six-digit space is small enough to
 * brute-force offline, so the per-challenge salt is what stops a leaked Firestore
 * export from being reversed with a rainbow table.
 */
export function hashCode(code: string, salt: string): string {
  return crypto.createHash("sha256").update(`${salt}:${code}`).digest("hex");
}

export function timingSafeEquals(a: string, b: string): boolean {
  const bufA = Buffer.from(a, "utf8");
  const bufB = Buffer.from(b, "utf8");
  if (bufA.length !== bufB.length) return false;
  return crypto.timingSafeEqual(bufA, bufB);
}

/** `juan.delacruz@gmail.com` -> `ju••••••••@gmail.com`, so the app can confirm where it went. */
export function maskEmail(email: string): string {
  const [local, domain] = email.split("@");
  if (!domain) return email;
  const head = local.slice(0, 2);
  return `${head}${"•".repeat(Math.max(local.length - 2, 1))}@${domain}`;
}

/** Mails a verification code. The template and the transport both live elsewhere. */
export async function sendOtpEmail(to: string, code: string): Promise<void> {
  await sendMail(to, otpEmail(code));
}

/**
 * An admin who revoked an account with setUserVerification would otherwise watch the
 * student walk straight back in through the OTP flow — it grants the same claim.
 * Rejected accounts are refused a code and refused a verification.
 */
async function assertNotBlocked(uid: string): Promise<void> {
  const snapshot = await db.collection("users").doc(uid).get();
  if (snapshot.data()?.["verificationStatus"] === "rejected") {
    throw new HttpsError(
      "permission-denied",
      "This account has been blocked by a NUTrade admin."
    );
  }
}

/**
 * Mails a fresh six-digit code to the signed-in account's own email address.
 *
 * The address is taken from the ID token, never from the request body — otherwise
 * this endpoint would happily mail codes to strangers on any signed-in user's say-so.
 */
export const sendEmailOtp = onCall({ secrets: mailSecrets, region: REGION }, async (request) => {
  const auth = request.auth;
  if (!auth) throw new HttpsError("unauthenticated", "Sign in required.");

  const email = auth.token.email;
  if (!email) throw new HttpsError("failed-precondition", "This account has no email address.");

  await assertNotBlocked(auth.uid);

  if (auth.token.verified === true) {
    return { alreadyVerified: true, sentTo: maskEmail(email) };
  }

  const challengeRef = db.collection("otpChallenges").doc(auth.uid);
  const existing = await challengeRef.get();
  const now = Timestamp.now();

  if (existing.exists) {
    const lastSentAt = existing.data()!.lastSentAt as Timestamp | undefined;
    if (lastSentAt) {
      const elapsedSeconds = (now.toMillis() - lastSentAt.toMillis()) / 1000;
      if (elapsedSeconds < OTP_RESEND_COOLDOWN_SECONDS) {
        throw new HttpsError(
          "resource-exhausted",
          `Wait ${Math.ceil(OTP_RESEND_COOLDOWN_SECONDS - elapsedSeconds)}s before asking for another code.`
        );
      }
    }
  }

  await consumeMailAllowance(email);

  const code = generateCode();
  const salt = crypto.randomBytes(16).toString("hex");

  // Written before the send: a code that reached the inbox but was never recorded
  // would be unusable, which is the worse of the two failure modes.
  await challengeRef.set({
    uid: auth.uid,
    email,
    salt,
    codeHash: hashCode(code, salt),
    attempts: 0,
    createdAt: now,
    lastSentAt: now,
    expiresAt: Timestamp.fromMillis(now.toMillis() + OTP_TTL_MINUTES * 60_000),
  });

  await sendOtpEmail(email, code);
  logger.info("sendEmailOtp: code issued", { uid: auth.uid });

  return {
    alreadyVerified: false,
    sentTo: maskEmail(email),
    expiresInSeconds: OTP_TTL_MINUTES * 60,
    resendAfterSeconds: OTP_RESEND_COOLDOWN_SECONDS,
  };
});

/**
 * Checks a submitted code and, on success, marks the account verified.
 *
 * This is the whole verification gate. Registration asks for no Student ID — the
 * `verified` claim granted here is what firestore.rules, placeBid and createQrPayment
 * check before letting anyone post or bid. It proves the student controls the email
 * address and nothing more; `setUserVerification` remains available to admins for
 * revoking an account after the fact.
 */
export const verifyEmailOtp = onCall({ region: REGION }, async (request) => {
  const auth = request.auth;
  if (!auth) throw new HttpsError("unauthenticated", "Sign in required.");

  const { code } = (request.data ?? {}) as { code?: string };
  const submitted = (code ?? "").trim();
  if (!/^\d{6}$/.test(submitted)) {
    throw new HttpsError("invalid-argument", "Enter the 6-digit code from your email.");
  }

  await assertNotBlocked(auth.uid);

  const challengeRef = db.collection("otpChallenges").doc(auth.uid);
  const snapshot = await challengeRef.get();
  if (!snapshot.exists) {
    throw new HttpsError("not-found", "That code has expired. Ask for a new one.");
  }

  const challenge = snapshot.data()!;
  const now = Timestamp.now();

  if ((challenge.expiresAt as Timestamp).toMillis() <= now.toMillis()) {
    await challengeRef.delete();
    throw new HttpsError("deadline-exceeded", "That code has expired. Ask for a new one.");
  }

  const attempts = (challenge.attempts as number) ?? 0;
  if (attempts >= OTP_MAX_ATTEMPTS) {
    await challengeRef.delete();
    throw new HttpsError("resource-exhausted", "Too many wrong codes. Ask for a new one.");
  }

  if (!timingSafeEquals(hashCode(submitted, challenge.salt as string), challenge.codeHash as string)) {
    await challengeRef.update({ attempts: attempts + 1 });
    const left = OTP_MAX_ATTEMPTS - (attempts + 1);
    throw new HttpsError(
      "permission-denied",
      left > 0 ? `That code isn't right. ${left} attempt${left === 1 ? "" : "s"} left.` : "That code isn't right."
    );
  }

  const existingClaims = (await getAuth().getUser(auth.uid)).customClaims ?? {};
  await getAuth().setCustomUserClaims(auth.uid, { ...existingClaims, verified: true });
  await getAuth().updateUser(auth.uid, { emailVerified: true });

  await db.collection("users").doc(auth.uid).set(
    { verificationStatus: "verified", verifiedAt: now, verifiedVia: "email_otp" },
    { merge: true }
  );

  await challengeRef.delete();
  logger.info("verifyEmailOtp: account verified", { uid: auth.uid });

  return { verified: true };
});

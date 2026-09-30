import * as crypto from "crypto";
import { onCall, HttpsError } from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import { getAuth } from "firebase-admin/auth";
import { Timestamp } from "firebase-admin/firestore";
import { db } from "./admin";
import { mailSecrets, sendMail } from "./brevo";
import { consumeMailAllowance } from "./mailRateLimit";
import { generateCode, hashCode, maskEmail, timingSafeEquals } from "./emailOtp";
import { passwordResetEmail } from "./mailTemplates";
import {
  OTP_MAX_ATTEMPTS,
  OTP_RESEND_COOLDOWN_SECONDS,
  PASSWORD_RESET_TOKEN_TTL_MINUTES,
  PASSWORD_RESET_TTL_MINUTES,
  REGION,
} from "./constants";

/**
 * Forgot password, the same shape as registration: email → six-digit code → new
 * password. The code goes out through Brevo.
 *
 * Firebase Auth's own `sendPasswordResetEmail` is deliberately not used. It mails from
 * Google's servers with Google's template and drops the student into a web page, so it
 * could not go through Brevo at all, and it would be the one flow in the app that leaves
 * the app. This keeps the student on one screen and every email on one provider.
 *
 * Like the signup callables these are open to signed-out callers by necessity. What
 * limits abuse: one code per address per OTP_RESEND_COOLDOWN_SECONDS, OTP_MAX_ATTEMPTS
 * guesses per code, and codes and tokens stored only as hashes in
 * `passwordResetChallenges`, which firestore.rules does not expose to clients at all.
 */

const MIN_PASSWORD_LENGTH = 8;

/** Said to everyone, whether or not the address has an account — see startPasswordReset. */
const START_AGAIN = "That reset has expired. Ask for a new code.";

function sha256(value: string): string {
  return crypto.createHash("sha256").update(value).digest("hex");
}

/** Lowercased and trimmed; the same shape check as NUTradeConstants.IsValidEmail. */
function normalizeEmail(raw: unknown): string {
  const email = typeof raw === "string" ? raw.trim().toLowerCase() : "";
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
    throw new HttpsError("invalid-argument", "Enter a valid email address.");
  }
  return email;
}

/** Keyed by a hash so the document id never carries the address itself. */
function challengeRef(email: string) {
  return db.collection("passwordResetChallenges").doc(sha256(email));
}

/**
 * The account behind an address, or null when there is none.
 *
 * A `rejected` account is treated as none at all: an admin who revoked it with
 * setUserVerification should not watch the student reset their way back in, and the
 * caller says nothing either way, so this reveals nothing a stranger could use.
 */
async function resettableUid(email: string): Promise<string | null> {
  let uid: string;
  try {
    ({ uid } = await getAuth().getUserByEmail(email));
  } catch (err) {
    if ((err as { code?: string }).code === "auth/user-not-found") return null;
    throw err;
  }

  const profile = await db.collection("users").doc(uid).get();
  if (profile.data()?.["verificationStatus"] === "rejected") {
    logger.warn("startPasswordReset: refused a blocked account", { uid });
    return null;
  }
  return uid;
}

/**
 * Step 1: mail a six-digit code to an address that has an account.
 *
 * The reply is the same whether or not the address is registered. Registration already
 * discloses that much — startSignup answers "an account with this email already exists" —
 * so this is not a guarantee about the system as a whole; it just declines to be a second
 * oracle, on the endpoint a stranger would reach for to test a list of addresses.
 */
export const startPasswordReset = onCall({ secrets: mailSecrets, region: REGION }, async (request) => {
  const email = normalizeEmail(request.data?.email);
  const quiet = {
    sentTo: maskEmail(email),
    expiresInSeconds: PASSWORD_RESET_TTL_MINUTES * 60,
    resendAfterSeconds: OTP_RESEND_COOLDOWN_SECONDS,
  };

  const ref = challengeRef(email);
  const now = Timestamp.now();

  const lastSentAt = (await ref.get()).data()?.lastSentAt as Timestamp | undefined;
  if (lastSentAt) {
    const elapsedSeconds = (now.toMillis() - lastSentAt.toMillis()) / 1000;
    if (elapsedSeconds < OTP_RESEND_COOLDOWN_SECONDS) {
      throw new HttpsError(
        "resource-exhausted",
        `Wait ${Math.ceil(OTP_RESEND_COOLDOWN_SECONDS - elapsedSeconds)}s before asking for another code.`
      );
    }
  }

  // Charged before the account lookup on purpose: an address with no account spends its
  // allowance too, so a caller cannot tell the two apart by whether the cap ever trips.
  await consumeMailAllowance(email);

  const uid = await resettableUid(email);
  if (!uid) {
    // No challenge document, so nothing to brute-force and nothing to fill Firestore
    // with. The student sees the same screen and simply never gets an email.
    logger.info("startPasswordReset: no account for that address");
    return quiet;
  }

  const code = generateCode();
  const salt = crypto.randomBytes(16).toString("hex");

  // Written before the send: a code that reached the inbox but was never recorded
  // would be unusable, which is the worse of the two failure modes.
  await ref.set({
    uid,
    email,
    salt,
    codeHash: hashCode(code, salt),
    attempts: 0,
    createdAt: now,
    lastSentAt: now,
    expiresAt: Timestamp.fromMillis(now.toMillis() + PASSWORD_RESET_TTL_MINUTES * 60_000),
    tokenHash: null,
    tokenExpiresAt: null,
  });

  await sendMail(email, passwordResetEmail(code));
  logger.info("startPasswordReset: code issued", { uid });

  return quiet;
});

/**
 * Step 2: check the code. Success spends the code and hands back a one-time reset
 * token — 256 random bits, stored only as a hash — that completePasswordReset requires.
 */
export const verifyPasswordResetCode = onCall({ region: REGION }, async (request) => {
  const email = normalizeEmail(request.data?.email);
  const submitted = String(request.data?.code ?? "").trim();
  if (!/^\d{6}$/.test(submitted)) {
    throw new HttpsError("invalid-argument", "Enter the 6-digit code from your email.");
  }

  const ref = challengeRef(email);
  const challenge = (await ref.get()).data();
  if (!challenge || !challenge.codeHash) {
    throw new HttpsError("not-found", "That code has expired. Ask for a new one.");
  }

  const now = Timestamp.now();
  if ((challenge.expiresAt as Timestamp).toMillis() <= now.toMillis()) {
    await ref.delete();
    throw new HttpsError("deadline-exceeded", "That code has expired. Ask for a new one.");
  }

  const attempts = (challenge.attempts as number) ?? 0;
  if (attempts >= OTP_MAX_ATTEMPTS) {
    await ref.delete();
    throw new HttpsError("resource-exhausted", "Too many wrong codes. Ask for a new one.");
  }

  if (!timingSafeEquals(hashCode(submitted, challenge.salt as string), challenge.codeHash as string)) {
    await ref.update({ attempts: attempts + 1 });
    const left = OTP_MAX_ATTEMPTS - (attempts + 1);
    throw new HttpsError(
      "permission-denied",
      left > 0 ? `That code isn't right. ${left} attempt${left === 1 ? "" : "s"} left.` : "That code isn't right."
    );
  }

  const token = crypto.randomBytes(32).toString("hex");
  await ref.update({
    codeHash: null,
    verifiedAt: now,
    tokenHash: sha256(token),
    tokenExpiresAt: Timestamp.fromMillis(now.toMillis() + PASSWORD_RESET_TOKEN_TTL_MINUTES * 60_000),
  });

  return { resetToken: token };
});

/**
 * Step 3: set the new password.
 *
 * Every other session is signed out at the same time. Someone resetting because their
 * password leaked is not helped by a new password that leaves the thief's existing
 * refresh token working.
 */
export const completePasswordReset = onCall({ region: REGION }, async (request) => {
  const data = request.data ?? {};
  const email = normalizeEmail(data.email);
  const password = typeof data.password === "string" ? data.password : "";
  if (password.length < MIN_PASSWORD_LENGTH) {
    throw new HttpsError("invalid-argument", `Use at least ${MIN_PASSWORD_LENGTH} characters for your password.`);
  }

  const ref = challengeRef(email);
  const challenge = (await ref.get()).data();
  const token = typeof data.resetToken === "string" ? data.resetToken : "";
  if (!challenge?.tokenHash || !token || !timingSafeEquals(sha256(token), challenge.tokenHash as string)) {
    throw new HttpsError("permission-denied", START_AGAIN);
  }
  if ((challenge.tokenExpiresAt as Timestamp).toMillis() <= Date.now()) {
    await ref.delete();
    throw new HttpsError("deadline-exceeded", START_AGAIN);
  }

  const uid = challenge.uid as string;
  try {
    await getAuth().updateUser(uid, { password });
  } catch (err) {
    const code = (err as { code?: string }).code;
    if (code === "auth/invalid-password") {
      throw new HttpsError("invalid-argument", "Choose a stronger password.");
    }
    // The account was deleted between the code and this call. Nothing to reset.
    if (code === "auth/user-not-found") {
      await ref.delete();
      throw new HttpsError("not-found", START_AGAIN);
    }
    throw err;
  }

  await getAuth().revokeRefreshTokens(uid);
  await ref.delete();
  logger.info("completePasswordReset: password changed", { uid });

  return { reset: true };
});

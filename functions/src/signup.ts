import * as crypto from "crypto";
import { onCall, HttpsError } from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import { getAuth } from "firebase-admin/auth";
import { Timestamp } from "firebase-admin/firestore";
import { db } from "./admin";
import { OTP_MAX_ATTEMPTS, OTP_RESEND_COOLDOWN_SECONDS, OTP_TTL_MINUTES, PROGRAMS, REGION } from "./constants";
import { mailSecrets } from "./brevo";
import { consumeMailAllowance } from "./mailRateLimit";
import { generateCode, hashCode, maskEmail, sendOtpEmail, timingSafeEquals } from "./emailOtp";

/**
 * Registration, code first: email → six-digit code → first name, last name, program
 * and password → account.
 *
 * Nothing is created until the code has been proven, so an abandoned sign-up leaves no
 * half-made account behind, and the account is born verified. (sendEmailOtp /
 * verifyEmailOtp still verify accounts that already exist — Google sign-in and older
 * accounts — and are unchanged.)
 *
 * These callables are open to signed-out callers by necessity. What limits abuse: one
 * code per address per OTP_RESEND_COOLDOWN_SECONDS, OTP_MAX_ATTEMPTS guesses per code,
 * and codes and tokens stored only as hashes in `signupChallenges`, which
 * firestore.rules does not expose to clients at all.
 */

/** How long a proven email stays usable for finishing the account. */
const SIGNUP_TOKEN_TTL_MINUTES = 30;
const MIN_PASSWORD_LENGTH = 8;
const MAX_NAME_LENGTH = 40;

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

function cleanName(raw: unknown, label: string): string {
  const name = typeof raw === "string" ? raw.trim().replace(/\s+/g, " ") : "";
  if (name.length === 0) throw new HttpsError("invalid-argument", `Enter your ${label}.`);
  if (name.length > MAX_NAME_LENGTH) {
    throw new HttpsError("invalid-argument", `Keep your ${label} under ${MAX_NAME_LENGTH} characters.`);
  }
  return name;
}

/** One of PROGRAMS, exactly — it is shown to other students under the name. */
function cleanProgram(raw: unknown): string {
  const program = typeof raw === "string" ? raw.trim() : "";
  if (!(PROGRAMS as readonly string[]).includes(program)) {
    throw new HttpsError("invalid-argument", "Choose your program.");
  }
  return program;
}

/** Keyed by a hash so the document id never carries the address itself. */
function challengeRef(email: string) {
  return db.collection("signupChallenges").doc(sha256(email));
}

async function emailIsRegistered(email: string): Promise<boolean> {
  try {
    await getAuth().getUserByEmail(email);
    return true;
  } catch (err) {
    if ((err as { code?: string }).code === "auth/user-not-found") return false;
    throw err;
  }
}

const EMAIL_TAKEN = "An account with this email already exists. Sign in instead.";
const START_AGAIN = "Your email confirmation has expired. Start again to get a new code.";

/** Step 1: mail a six-digit code to an address that has no account yet. */
export const startSignup = onCall({ secrets: mailSecrets, region: REGION }, async (request) => {
  const email = normalizeEmail(request.data?.email);
  if (await emailIsRegistered(email)) throw new HttpsError("already-exists", EMAIL_TAKEN);

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

  await consumeMailAllowance(email);

  const code = generateCode();
  const salt = crypto.randomBytes(16).toString("hex");

  // Written before the send: a code that reached the inbox but was never recorded
  // would be unusable, which is the worse of the two failure modes.
  await ref.set({
    email,
    salt,
    codeHash: hashCode(code, salt),
    attempts: 0,
    createdAt: now,
    lastSentAt: now,
    expiresAt: Timestamp.fromMillis(now.toMillis() + OTP_TTL_MINUTES * 60_000),
    tokenHash: null,
    tokenExpiresAt: null,
  });

  await sendOtpEmail(email, code);
  logger.info("startSignup: code issued", { challenge: ref.id });

  return {
    sentTo: maskEmail(email),
    expiresInSeconds: OTP_TTL_MINUTES * 60,
    resendAfterSeconds: OTP_RESEND_COOLDOWN_SECONDS,
  };
});

/**
 * Step 2: check the code. Success spends the code and hands back a one-time signup
 * token — 256 random bits, stored only as a hash — that completeSignup requires.
 */
export const verifySignupCode = onCall({ region: REGION }, async (request) => {
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
    tokenExpiresAt: Timestamp.fromMillis(now.toMillis() + SIGNUP_TOKEN_TTL_MINUTES * 60_000),
  });

  return { signupToken: token };
});

/**
 * Step 3: create the account — already verified, since the email was proven in step 2 —
 * and its profile. The student then signs in with the email and password.
 */
export const completeSignup = onCall({ region: REGION }, async (request) => {
  const data = request.data ?? {};
  const email = normalizeEmail(data.email);
  const firstName = cleanName(data.firstName, "first name");
  const lastName = cleanName(data.lastName, "last name");
  const program = cleanProgram(data.program);
  const password = typeof data.password === "string" ? data.password : "";
  if (password.length < MIN_PASSWORD_LENGTH) {
    throw new HttpsError("invalid-argument", `Use at least ${MIN_PASSWORD_LENGTH} characters for your password.`);
  }

  const ref = challengeRef(email);
  const challenge = (await ref.get()).data();
  const token = typeof data.signupToken === "string" ? data.signupToken : "";
  if (!challenge?.tokenHash || !token || !timingSafeEquals(sha256(token), challenge.tokenHash as string)) {
    throw new HttpsError("permission-denied", START_AGAIN);
  }
  if ((challenge.tokenExpiresAt as Timestamp).toMillis() <= Date.now()) {
    await ref.delete();
    throw new HttpsError("deadline-exceeded", START_AGAIN);
  }

  const displayName = `${firstName} ${lastName}`;
  let uid: string;
  try {
    ({ uid } = await getAuth().createUser({ email, password, displayName, emailVerified: true }));
  } catch (err) {
    const code = (err as { code?: string }).code;
    if (code === "auth/email-already-exists") throw new HttpsError("already-exists", EMAIL_TAKEN);
    if (code === "auth/invalid-password") throw new HttpsError("invalid-argument", "Choose a stronger password.");
    throw err;
  }

  await getAuth().setCustomUserClaims(uid, { verified: true });

  const now = Timestamp.now();
  await db.collection("users").doc(uid).set({
    uid,
    email,
    displayName,
    firstName,
    lastName,
    studentId: "",
    campusHub: "Unknown",
    program,
    photoUrl: null,
    tradesCompleted: 0,
    rating: null,
    role: "student",
    verificationStatus: "verified",
    verifiedAt: now,
    verifiedVia: "email_otp",
    fcmTokens: [],
    createdAt: now,
  });

  await ref.delete();
  logger.info("completeSignup: account created", { uid });

  return { uid };
});

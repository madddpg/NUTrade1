import * as crypto from "crypto";
import { HttpsError } from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import { Timestamp } from "firebase-admin/firestore";
import { db } from "./admin";
import { OTP_MAX_SENDS_PER_WINDOW, OTP_SEND_WINDOW_MINUTES } from "./constants";

/**
 * A rolling-window cap on how many codes one address can be mailed, on top of the
 * per-send cooldown.
 *
 * The cooldown alone only sets the *rate*: a script could still hold an address at one
 * code a minute indefinitely, which is 1,440 emails a day into someone's inbox, all of
 * them from us and all of them counting against the Brevo plan. This sets the *total*.
 *
 * Counters live in `mailRateLimits/{sha256(email)}`, separate from the challenge
 * documents on purpose — those are deleted when a code is spent, expires or is burned by
 * wrong guesses, which would hand the limit straight back. firestore.rules does not
 * expose this collection to clients at all.
 *
 * Both signed-in and signed-out senders key on the address rather than the uid or the
 * caller's IP: the address is what receives the mail, and it is the one identifier
 * present in all three flows (sendEmailOtp, startSignup, startPasswordReset).
 */

const WINDOW_MS = OTP_SEND_WINDOW_MINUTES * 60_000;

/**
 * How long a spent counter is kept. A Firestore TTL policy on `expiresAt` sweeps these —
 * without one the collection grows one document per address forever. See the README.
 */
const RETENTION_MS = WINDOW_MS * 2;

function rateLimitRef(email: string) {
  const hash = crypto.createHash("sha256").update(email).digest("hex");
  return db.collection("mailRateLimits").doc(hash);
}

/** Minutes, for a message a student can act on. */
function retryAfterText(windowEndsAt: number): string {
  const minutes = Math.max(1, Math.ceil((windowEndsAt - Date.now()) / 60_000));
  return minutes === 1 ? "a minute" : `${minutes} minutes`;
}

/**
 * Charges one send against `email`'s allowance, or throws `resource-exhausted`.
 *
 * Called *before* the send and in one transaction, so two requests racing cannot both
 * see the last slot. A send that then fails is not refunded: retrying a mailer that just
 * rejected us is not what we want to make cheap, and the window rolls off on its own.
 */
export async function consumeMailAllowance(email: string): Promise<void> {
  const ref = rateLimitRef(email);

  const exhaustedUntil = await db.runTransaction(async (tx) => {
    const snapshot = await tx.get(ref);
    const now = Timestamp.now();
    const data = snapshot.data();

    const startedAt = data?.["windowStartedAt"] as Timestamp | undefined;
    const windowIsCurrent = startedAt !== undefined && now.toMillis() - startedAt.toMillis() < WINDOW_MS;
    const sent = windowIsCurrent ? ((data?.["sent"] as number | undefined) ?? 0) : 0;

    if (sent >= OTP_MAX_SENDS_PER_WINDOW) {
      // Reported by the caller, not thrown here: throwing inside a transaction would
      // have it retried before the error surfaced.
      return startedAt!.toMillis() + WINDOW_MS;
    }

    const windowStartedAt = windowIsCurrent ? startedAt! : now;
    tx.set(ref, {
      windowStartedAt,
      sent: sent + 1,
      lastSentAt: now,
      expiresAt: Timestamp.fromMillis(windowStartedAt.toMillis() + RETENTION_MS),
    });
    return null;
  });

  if (exhaustedUntil !== null) {
    logger.warn("mail rate limit hit", { challenge: ref.id });
    throw new HttpsError(
      "resource-exhausted",
      `Too many codes requested for this email. Try again in ${retryAfterText(exhaustedUntil)}.`
    );
  }
}

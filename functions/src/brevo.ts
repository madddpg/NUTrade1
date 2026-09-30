import { HttpsError } from "firebase-functions/v2/https";
import { defineSecret } from "firebase-functions/params";
import * as logger from "firebase-functions/logger";
import { brevoSender } from "./constants";

/**
 * Every email NUTrade sends — verification codes and password-reset codes — goes out
 * through Brevo's transactional API.
 *
 * Brevo replaced Gmail SMTP: an App Password on a personal Gmail account caps out around
 * 500 messages a day, sends as that account's own address, and offers no delivery log to
 * look at when a student swears no code arrived. Brevo sends from our own validated
 * sender, allows far more, and shows every message in its dashboard.
 *
 * The API is one POST, so there is no SDK here — Node 20 has global `fetch`, and a
 * dependency for a single endpoint is a dependency to keep patched for nothing.
 */
export const brevoApiKey = defineSecret("BREVO_API_KEY");

/** For the `secrets:` list of every function that mails a code. */
export const mailSecrets = [brevoApiKey];

const BREVO_ENDPOINT = "https://api.brevo.com/v3/smtp/email";

/** Brevo is normally well under a second; past this the student is better off retrying. */
const SEND_TIMEOUT_MS = 10_000;

export interface MailMessage {
  subject: string;
  text: string;
  html: string;
}

/**
 * Hands one message to Brevo. Throws an `HttpsError` the caller can surface
 * as-is — the address belongs to the student, so a rejection is almost always ours (an
 * unvalidated sender, a revoked key, or the plan's daily cap).
 */
export async function sendMail(to: string, message: MailMessage): Promise<void> {
  const key = brevoApiKey.value();
  if (!key || key.startsWith("REPLACE_ME")) {
    throw new HttpsError(
      "failed-precondition",
      "Email delivery isn't configured yet. Set the BREVO_API_KEY secret."
    );
  }

  const sender = brevoSender();
  if (!sender) {
    throw new HttpsError(
      "failed-precondition",
      "Email delivery isn't configured yet. Set BREVO_SENDER_EMAIL in functions/.env."
    );
  }

  let response: Response;
  try {
    response = await fetch(BREVO_ENDPOINT, {
      method: "POST",
      headers: {
        "api-key": key,
        "content-type": "application/json",
        accept: "application/json",
      },
      body: JSON.stringify({
        sender,
        to: [{ email: to }],
        subject: message.subject,
        textContent: message.text,
        htmlContent: message.html,
      }),
      signal: AbortSignal.timeout(SEND_TIMEOUT_MS),
    });
  } catch (err) {
    logger.error("Brevo was unreachable", { err });
    throw new HttpsError("unavailable", "Could not send the code. Try again in a moment.");
  }

  if (response.ok) return;

  // Brevo answers a rejection with {"code": "...", "message": "..."} — worth logging in
  // full, because `invalid_parameter` on the sender and an expired key read identically
  // from the app's side.
  const body = await response.text().catch(() => "");
  logger.error("Brevo rejected the email", {
    status: response.status,
    body: body.slice(0, 500),
    sender: sender.email,
  });

  if (response.status === 401) {
    throw new HttpsError(
      "failed-precondition",
      "Email delivery isn't configured correctly. The BREVO_API_KEY secret was rejected."
    );
  }
  throw new HttpsError("internal", "Could not send the code. Try again in a moment.");
}

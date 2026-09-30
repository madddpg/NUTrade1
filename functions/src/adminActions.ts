import { onCall, onRequest, HttpsError } from "firebase-functions/v2/https";
import { defineSecret } from "firebase-functions/params";
import * as logger from "firebase-functions/logger";
import * as crypto from "crypto";
import { getAuth } from "firebase-admin/auth";
import { Timestamp } from "firebase-admin/firestore";
import { db } from "./admin";
import { REGION } from "./constants";

export const adminBootstrapSecret = defineSecret("ADMIN_BOOTSTRAP_SECRET");

/** Constant-time compare so the bootstrap secret can't be recovered by timing the endpoint. */
function secretMatches(supplied: string, expected: string): boolean {
  const a = Buffer.from(supplied, "utf8");
  const b = Buffer.from(expected, "utf8");
  if (a.length !== b.length) return false;
  return crypto.timingSafeEqual(a, b);
}

/**
 * Chicken-and-egg breaker: `setUserVerification` is admin-only, but nothing can mint
 * the first admin from inside the app. This endpoint promotes one account to admin,
 * authorised by a Secret Manager value rather than by a claim.
 *
 * Operational, not a product feature — it stays deployed only as long as you need it,
 * and the secret should be rotated (or the function deleted) once the real admin
 * portal owns this. See docs/architecture-and-security.md §2.B.
 */
export const bootstrapAdmin = onRequest(
  { secrets: [adminBootstrapSecret], region: REGION },
  async (req, res) => {
    const supplied = req.header("X-Bootstrap-Secret");
    if (!supplied || !secretMatches(supplied, adminBootstrapSecret.value())) {
      logger.warn("bootstrapAdmin: bad secret");
      res.status(401).send("Unauthorized.");
      return;
    }

    const email = (req.body?.email as string | undefined)?.trim();
    if (!email) {
      res.status(400).send("Body must be JSON with an `email` field.");
      return;
    }

    try {
      const user = await getAuth().getUserByEmail(email);
      await getAuth().setCustomUserClaims(user.uid, { role: "admin", verified: true });
      await db.collection("users").doc(user.uid).set(
        { role: "admin", verificationStatus: "verified", verifiedAt: Timestamp.now() },
        { merge: true }
      );
      logger.info("bootstrapAdmin: promoted", { uid: user.uid, email });
      res.status(200).json({ uid: user.uid, email, role: "admin", verified: true });
    } catch (err) {
      logger.error("bootstrapAdmin: failed", { email, err });
      res.status(404).send("No account with that email.");
    }
  }
);

/**
 * The decision the admin portal's ID-review queue makes: after eyeballing the
 * uploaded Student ID photo, flip the account's `verified` custom claim. Only a
 * verified account can create a listing or place a bid (see firestore.rules and
 * the guards in placeBid / createQrPayment).
 */
export const setUserVerification = onCall(
  { region: REGION },
  async (request) => {
    const auth = request.auth;
    if (!auth) throw new HttpsError("unauthenticated", "Sign in required.");
    if (auth.token.role !== "admin") {
      throw new HttpsError("permission-denied", "Admins only.");
    }

    const { uid, verified, reason } = (request.data ?? {}) as {
      uid?: string;
      verified?: boolean;
      reason?: string;
    };
    if (!uid || typeof verified !== "boolean") {
      throw new HttpsError("invalid-argument", "uid and a boolean `verified` are required.");
    }

    const existing = (await getAuth().getUser(uid)).customClaims ?? {};
    await getAuth().setCustomUserClaims(uid, { ...existing, verified });
    await db.collection("users").doc(uid).set(
      {
        verificationStatus: verified ? "verified" : "rejected",
        verificationReason: reason ?? null,
        verifiedAt: verified ? Timestamp.now() : null,
        reviewedBy: auth.uid,
      },
      { merge: true }
    );

    logger.info("setUserVerification", { uid, verified, reviewedBy: auth.uid });
    return { uid, verified };
  }
);

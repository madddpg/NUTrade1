import { onCall, HttpsError } from "firebase-functions/v2/https";
import { defineSecret } from "firebase-functions/params";
import { Timestamp } from "firebase-admin/firestore";
import { db } from "./admin";
import { PayMongoClient, extractQrFromNextAction, paymentUnavailable, paymongoTestMode } from "./paymongo";
import { refreshQrOnDoc } from "./qrRefresh";
import {
  feeForPackage,
  FREE_POST_USED_STATUSES,
  QR_EXPIRY_SECONDS,
  ListingPackage,
  LISTING_STATUS,
  REGION,
} from "./constants";
import { pendingApprovalFields } from "./listingApproval";
import { reconcileListingFees } from "./listingFees";

export const paymongoSecretKey = defineSecret("PAYMONGO_SECRET_KEY");

interface CreateQrPaymentRequest {
  listingId: string;
  package: ListingPackage;
}

/**
 * Brokers the listing-posting fee. A verified student's first listing ever may use the
 * Free package, which goes straight to the admin queue with no PayMongo call; every
 * other case creates a PayMongo QR Ph payment and returns the code to render. Either
 * way nothing goes live here — an admin approves it first (see listingApproval.ts).
 * The client never talks to PayMongo directly — see docs/architecture-and-security.md §2.C.3.
 */
export const createQrPayment = onCall(
  { secrets: [paymongoSecretKey], region: "asia-southeast1" },
  async (request) => {
    const auth = request.auth;
    if (!auth) throw new HttpsError("unauthenticated", "Sign in required.");
    if (auth.token.verified !== true) {
      throw new HttpsError("permission-denied", "Your account isn't verified yet.");
    }

    const { listingId, package: pkg } = (request.data ?? {}) as Partial<CreateQrPaymentRequest>;
    if (!listingId || !pkg) {
      throw new HttpsError("invalid-argument", "listingId and package are required.");
    }

    const listingRef = db.collection("listings").doc(listingId);
    const listingSnap = await listingRef.get();
    if (!listingSnap.exists) throw new HttpsError("not-found", "Listing not found.");
    const listing = listingSnap.data()!;

    if (listing.ownerUid !== auth.uid) {
      throw new HttpsError("permission-denied", "This isn't your listing.");
    }
    if (listing.status === LISTING_STATUS.pendingApproval) {
      throw new HttpsError("failed-precondition", "This listing is already posted and waiting for admin approval.");
    }
    if (![LISTING_STATUS.draft, LISTING_STATUS.pendingPayment].includes(listing.status)) {
      throw new HttpsError("failed-precondition", "This listing is already published or closed.");
    }

    const client = new PayMongoClient(paymongoSecretKey.value());

    // A fee PayMongo already took for this listing — its webhook never arrived, or it
    // cleared after the QR sweep gave up on it — is honoured before anything else, so
    // reopening the payment screen never charges a student twice.
    if (await reconcileListingFees(client, listingId)) {
      return { requiresPayment: false, listingId, status: LISTING_STATUS.pendingApproval, alreadyPaid: true };
    }

    // Free is a student's first listing, once ever (FREE_POST_USED_STATUSES). Checked
    // and claimed in one transaction so two drafts submitted at the same moment are far
    // less likely to both come through free.
    if (pkg === "Free") {
      await db.runTransaction(async (tx) => {
        const used = await tx.get(
          db
            .collection("listings")
            .where("ownerUid", "==", auth.uid)
            .where("status", "in", FREE_POST_USED_STATUSES)
            .limit(1)
        );
        // Refuse explicitly rather than letting it fall through to PayMongo, which would
        // be asked to mint a QR code for zero pesos and would reject it with an error no
        // student could act on.
        if (!used.empty) {
          throw new HttpsError(
            "failed-precondition",
            "You've already used your free first post. Choose Additional (₱10) or Priority (₱20) for this one."
          );
        }
        tx.update(listingRef, pendingApprovalFields(pkg, Timestamp.now()));
      });
      return { requiresPayment: false, listingId, status: LISTING_STATUS.pendingApproval };
    }

    const amountCentavos = feeForPackage(pkg);

    // Read the profile first so the try below wraps PayMongo and nothing else — a
    // Firestore failure reported as "the payment provider is down" would send whoever
    // debugs it to the wrong place.
    const ownerProfileSnap = await db.collection("users").doc(auth.uid).get();
    const ownerProfile = ownerProfileSnap.data() ?? {};

    let intent, qr;
    try {
      intent = await client.createPaymentIntent(
        amountCentavos,
        `NUTrade listing fee — ${listingId} (${pkg})`
      );

      const method = await client.createQrPhPaymentMethod(
        (ownerProfile.displayName as string) ?? "NUTrade student",
        (ownerProfile.email as string) ?? auth.token.email ?? ""
      );

      const clientKey = intent.data.attributes["client_key"] as string;
      const attached = await client.attachPaymentMethod(intent.data.id, method.data.id, clientKey);
      qr = extractQrFromNextAction(attached);
    } catch (err) {
      throw paymentUnavailable(err);
    }

    // PayMongo says when the code stops working (about 30 minutes, seen live). Using its
    // time rather than our own guess keeps expireStalePayments from giving up on a code
    // the student can still pay.
    const now = Timestamp.now();
    const paymongoExpiry = qr.expiresAt ? Date.parse(qr.expiresAt) : NaN;
    const expiresAt = Number.isNaN(paymongoExpiry)
      ? Timestamp.fromMillis(now.toMillis() + QR_EXPIRY_SECONDS * 1000)
      : Timestamp.fromMillis(paymongoExpiry);

    const paymentRef = db.collection("payments").doc();
    const testMode = paymongoTestMode(paymongoSecretKey.value());
    await paymentRef.set({
      listingId,
      uid: auth.uid,
      package: pkg,
      amount: amountCentavos,
      paymongoIntentId: intent.data.id,
      status: "awaiting_payment",
      qrImageUrl: qr.qrImageUrl ?? null,
      qrImageBase64: qr.qrImageBase64 ?? null,
      qrPayload: qr.qrPayload ?? null,
      paymongoTestMode: testMode,
      qrExpiresAt: expiresAt,
      createdAt: now,
    });

    await listingRef.update({ status: "pending_payment" });

    return {
      requiresPayment: true,
      listingId,
      paymentId: paymentRef.id,
      qrImageUrl: qr.qrImageUrl ?? null,
      qrImageBase64: qr.qrImageBase64 ?? null,
      qrPayload: qr.qrPayload ?? null,
      redirectUrl: qr.redirectUrl ?? null,
      amountCentavos,
      expiresAt: expiresAt.toMillis(),
      testMode,
    };
  }
);

/**
 * "Has my payment gone through yet?" — polled by the payment screen while the QR is up,
 * and behind its "I've paid" button. Asks PayMongo about this listing's unsettled fee
 * payments and settles one that was paid, so a student who paid is never left waiting
 * on a webhook that is late, misconfigured or never comes. Returns the listing's status
 * afterwards; the app follows the listing document either way.
 */
export const checkListingPayment = onCall(
  { secrets: [paymongoSecretKey], region: REGION },
  async (request) => {
    const auth = request.auth;
    if (!auth) throw new HttpsError("unauthenticated", "Sign in required.");

    const { listingId } = (request.data ?? {}) as { listingId?: unknown };
    if (typeof listingId !== "string" || listingId.length === 0) {
      throw new HttpsError("invalid-argument", "listingId is required.");
    }

    const listingRef = db.collection("listings").doc(listingId);
    const listingSnap = await listingRef.get();
    if (!listingSnap.exists) throw new HttpsError("not-found", "Listing not found.");
    const listing = listingSnap.data()!;

    if (listing.ownerUid !== auth.uid && auth.token.role !== "admin") {
      throw new HttpsError("permission-denied", "This isn't your listing.");
    }

    const client = new PayMongoClient(paymongoSecretKey.value());
    const testMode = paymongoTestMode(paymongoSecretKey.value());
    if ([LISTING_STATUS.draft, LISTING_STATUS.pendingPayment].includes(listing.status)) {
      await reconcileListingFees(client, listingId);
    }

    const listingStatus = (await listingRef.get()).data()?.status ?? listing.status;
    if (![LISTING_STATUS.draft, LISTING_STATUS.pendingPayment].includes(listingStatus)) {
      return { listingId, status: listingStatus, replaced: false, testMode: false };
    }

    // A failed scan leaves the intent open and that QR dead. Hand back a new
    // code when PayMongo says the last attempt failed, including docs an older
    // webhook already stamped `failed`.
    const payments = await db.collection("payments").where("listingId", "==", listingId).get();
    const open = payments.docs
      .map((doc) => ({ ref: doc.ref, data: doc.data() }))
      .filter((p) =>
        ["awaiting_payment", "expired", "failed"].includes(p.data.status as string) && p.data.paymongoIntentId
      )
      .sort((a, b) => {
        const aAt = (a.data.createdAt as { toMillis?: () => number } | undefined)?.toMillis?.() ?? 0;
        const bAt = (b.data.createdAt as { toMillis?: () => number } | undefined)?.toMillis?.() ?? 0;
        return bAt - aAt;
      })[0];

    if (!open) {
      return { listingId, status: listingStatus, replaced: false, testMode };
    }

    const qr = await refreshQrOnDoc(
      client,
      open.ref,
      open.data,
      open.data.paymongoIntentId as string,
      open.data.uid as string
    );
    return {
      listingId,
      status: listingStatus,
      qrImageUrl: qr.qrImageUrl,
      qrImageBase64: qr.qrImageBase64,
      qrPayload: qr.qrPayload,
      expiresAt: qr.expiresAt,
      replaced: qr.replaced,
      testMode: qr.testMode || testMode,
    };
  }
);

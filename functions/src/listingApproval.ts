import { onCall, HttpsError, CallableRequest } from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import { Timestamp } from "firebase-admin/firestore";
import { db } from "./admin";
import {
  AUCTION_DURATION_HOURS,
  LISTING_KIND,
  LISTING_STATUS,
  ListingKindName,
  ListingPackage,
  REGION,
  listingKind,
  normalizeListingPackage,
} from "./constants";
import { visibilityFor } from "./listingVisibility";

/**
 * Every new listing is moderated. Paying the posting fee — or qualifying for the free
 * first post — no longer publishes an auction; it hands it to an admin, and only
 * approveListing puts it on the feed.
 *
 * The auction clock starts at approval, not at payment, so time spent waiting in
 * the queue never eats into a seller's 24 hours. Approval also puts the listing
 * on the feed. Only a Priority package is pinned.
 */

/** Hard cap on an admin's rejection note, which the seller sees verbatim. */
const MAX_REJECTION_REASON_LENGTH = 300;

/**
 * The fields that move a listing into the admin queue. Used by createQrPayment (free
 * post) and paymongoWebhook (paid post) — the two places a listing's fee is settled.
 */
export function pendingApprovalFields(pkg: ListingPackage, now: Timestamp) {
  return {
    status: LISTING_STATUS.pendingApproval,
    // What was actually paid for. The listing's own `package` is whatever the client
    // wrote on the draft; this is the one approval honours.
    paidPackage: pkg,
    submittedForApprovalAt: now,
  };
}

/**
 * The fields that put an approved listing live: visible to every student, pinned
 * only when the paid package is Priority, and — for an auction only — the 24-hour
 * clock. A standard or swap listing must not receive `auctionEndsAt`, or the closer
 * will treat it as an auction that has already ended.
 */
export function goLiveFields(pkg: unknown, now: Timestamp, kind: ListingKindName = "Auction") {
  const normalized = normalizeListingPackage(pkg);
  const live = {
    status: LISTING_STATUS.active,
    publishedAt: now,
    isPinned: normalized === "Priority",
    ...visibilityFor(now.toMillis()),
  };
  if (kind !== LISTING_KIND.auction) return { ...live, auctionEndsAt: null };
  return {
    ...live,
    auctionEndsAt: Timestamp.fromMillis(now.toMillis() + AUCTION_DURATION_HOURS * 3_600_000),
  };
}

function requireAdmin(request: CallableRequest): string {
  const auth = request.auth;
  if (!auth) throw new HttpsError("unauthenticated", "Sign in required.");
  // role: the app admin claim. admin: the web admin panel's, which shares this database.
  if (auth.token.role !== "admin" && auth.token.admin !== true) {
    throw new HttpsError("permission-denied", "Admins only.");
  }
  return auth.uid;
}

function requireListingId(request: CallableRequest): string {
  const { listingId } = (request.data ?? {}) as { listingId?: unknown };
  if (typeof listingId !== "string" || listingId.length === 0) {
    throw new HttpsError("invalid-argument", "listingId is required.");
  }
  return listingId;
}

/**
 * Loads a listing inside `tx` and insists it is still waiting for review, so two
 * admins acting on the same listing cannot both win.
 */
async function readPendingListing(tx: FirebaseFirestore.Transaction, listingId: string) {
  const ref = db.collection("listings").doc(listingId);
  const snap = await tx.get(ref);
  if (!snap.exists) throw new HttpsError("not-found", "Listing not found.");
  const listing = snap.data()!;
  if (listing.status !== LISTING_STATUS.pendingApproval) {
    throw new HttpsError("failed-precondition", "This listing isn't waiting for approval any more.");
  }
  return { ref, listing };
}

/** Admin approves a queued listing: it goes live and its 24-hour auction starts now. */
export const approveListing = onCall({ region: REGION }, async (request) => {
  const adminUid = requireAdmin(request);
  const listingId = requireListingId(request);

  const outcome = await db.runTransaction(async (tx) => {
    const { ref, listing } = await readPendingListing(tx, listingId);
    const now = Timestamp.now();
    const live = goLiveFields(listing.paidPackage, now, listingKind(listing));

    tx.update(ref, { ...live, approvedAt: now, approvedBy: adminUid });
    return { ownerUid: listing.ownerUid as string, isVisible: live.isVisible };
  });

  // The seller is told by onListingUpdated, which also covers approvals made in the
  // web admin panel.
  logger.info("approveListing", { listingId, approvedBy: adminUid });

  return { listingId, isVisible: outcome.isVisible };
});

/**
 * Admin turns a queued listing down. Terminal — the seller posts a corrected listing
 * rather than editing this one.
 *
 * A paid posting fee is NOT refunded automatically: PayMongo refunds are a manual
 * step in the PayMongo dashboard for now.
 */
export const rejectListing = onCall({ region: REGION }, async (request) => {
  const adminUid = requireAdmin(request);
  const listingId = requireListingId(request);

  const { reason } = (request.data ?? {}) as { reason?: unknown };
  const note = typeof reason === "string" ? reason.trim().slice(0, MAX_REJECTION_REASON_LENGTH) : "";

  const ownerUid = await db.runTransaction(async (tx) => {
    const { ref, listing } = await readPendingListing(tx, listingId);
    tx.update(ref, {
      status: LISTING_STATUS.rejected,
      rejectionReason: note || null,
      rejectedAt: Timestamp.now(),
      rejectedBy: adminUid,
    });
    return listing.ownerUid as string;
  });

  // The seller is told by onListingUpdated, as for approvals.
  logger.info("rejectListing", { listingId, rejectedBy: adminUid, ownerUid });

  return { listingId };
});

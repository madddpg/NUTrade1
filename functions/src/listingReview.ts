import { onDocumentUpdated } from "firebase-functions/v2/firestore";
import * as logger from "firebase-functions/logger";
import { DocumentReference, DocumentData, Timestamp } from "firebase-admin/firestore";
import { db } from "./admin";
import { AUCTION_DURATION_HOURS, LISTING_STATUS, ListingPackage, REGION } from "./constants";
import { paymongoSecretKey } from "./createQrPayment";
import { goLiveFields } from "./listingApproval";
import { verifyListingFees } from "./listingFees";
import { notifyUser } from "./notify";
import { PayMongoClient } from "./paymongo";

/**
 * What has to follow a listing's review or submission, whoever made the change.
 *
 * The web admin panel reviews listings through approveListing / rejectListing, but it
 * also has its own copies of those callables and runs its own paymongoWebhook, which
 * moves a listing into the queue on any "payment.paid" event — including one sent
 * without a signature. Neither knows about the feed slot, the Priority pin, the app's
 * notifications or the fee record. This trigger fills those in from the listing
 * itself, so an approval ends the same way whichever code made it, and a queued
 * listing only stays queued if PayMongo says it was paid.
 */
export const onListingUpdated = onDocumentUpdated(
  { document: "listings/{listingId}", region: REGION, secrets: [paymongoSecretKey] },
  async (event) => {
    const before = event.data?.before.data();
    const after = event.data?.after.data();
    if (!before || !after) return;

    const ref = event.data!.after.ref;
    const listingId = event.params.listingId;

    if (before.status === LISTING_STATUS.pendingApproval && after.status === LISTING_STATUS.active) {
      await onApproved(ref, listingId, after);
    } else if (before.status === LISTING_STATUS.pendingApproval && after.status === LISTING_STATUS.rejected) {
      await onRejected(listingId, after);
    } else if (
      (before.status === LISTING_STATUS.pendingPayment || before.status === LISTING_STATUS.draft) &&
      after.status === LISTING_STATUS.pendingApproval &&
      !after.paidPackage
    ) {
      // createQrPayment and settleListingFee always stamp paidPackage, so a listing that
      // reached the queue without one got there through the panel's webhook.
      await onUnverifiedSubmission(ref, listingId, before.status as string);
    }
  }
);

async function onApproved(ref: DocumentReference, listingId: string, listing: DocumentData) {
  let isVisible = listing.isVisible === true;

  // The app's approveListing always sets visibleFrom; the panel's own approveListing
  // doesn't, and without it publishScheduledListings can never put the listing on the feed.
  if (listing.visibleFrom == null) {
    const pkg = (listing.paidPackage as ListingPackage | undefined) ?? "Free";
    const approvedAt = (listing.approvedAt as Timestamp | undefined) ?? Timestamp.now();
    const live = goLiveFields(pkg, approvedAt);

    // The panel's own countdown is kept where it wrote one — the seller may already
    // have seen it — and the feed slot counts from the panel's approval, not from now.
    // It has written auctionEndsAt as an ISO string, which closeExpiredAuctions' range
    // query and the app both skip, so only a real timestamp is kept as-is.
    await ref.update({
      isPinned: live.isPinned,
      isVisible: live.isVisible,
      visibleFrom: live.visibleFrom,
      publishedAt: asTimestamp(listing.publishedAt) ?? approvedAt,
      auctionEndsAt: asTimestamp(listing.auctionEndsAt) ??
        Timestamp.fromMillis(approvedAt.toMillis() + AUCTION_DURATION_HOURS * 3_600_000),
    });
    isVisible = live.isVisible;
    logger.info("onListingUpdated: completed a web-panel approval", { listingId, pkg });
  }

  await notifyUser(listing.ownerUid as string, {
    title: "Your listing was approved",
    body: isVisible
      ? "Your auction is now live on the campus feed."
      : "Your auction joins the campus feed at the next hourly refresh.",
    data: { listingId, type: "listing_approved" },
  });
}

/** A Firestore timestamp, or an ISO string the panel wrote in its place; else undefined. */
function asTimestamp(value: unknown): Timestamp | undefined {
  if (value instanceof Timestamp) return value;
  if (typeof value === "string") {
    const millis = Date.parse(value);
    if (!Number.isNaN(millis)) return Timestamp.fromMillis(millis);
  }
  return undefined;
}

async function onRejected(listingId: string, listing: DocumentData) {
  const note = typeof listing.rejectionReason === "string" ? listing.rejectionReason.trim() : "";
  await notifyUser(listing.ownerUid as string, {
    title: "Your listing wasn't approved",
    body: note || "An admin reviewed your listing and didn't approve it.",
    data: { listingId, type: "listing_rejected" },
  });
}

async function onUnverifiedSubmission(ref: DocumentReference, listingId: string, previousStatus: string) {
  const verdict = await verifyListingFees(new PayMongoClient(paymongoSecretKey.value()), listingId);

  // Paid: settleListingFee has already stamped paidPackage and recorded the fee.
  if (verdict === "paid") return;

  // PayMongo couldn't be asked. Leave it for the admin rather than undo a real payment;
  // expireStalePayments asks again about the payment doc on its next sweep.
  if (verdict === "unknown") {
    logger.error("onListingUpdated: queued by the panel's webhook, PayMongo unreachable", { listingId });
    return;
  }

  // PayMongo has no paid intent for this listing: the event was not a real payment.
  // Put it back where it was, unless something has moved it on since.
  const reverted = await db.runTransaction(async (tx) => {
    const current = (await tx.get(ref)).data();
    if (current?.status !== LISTING_STATUS.pendingApproval || current.paidPackage) return false;
    tx.update(ref, { status: previousStatus });
    return true;
  });
  logger.warn("onListingUpdated: queued without a payment PayMongo recognises", { listingId, reverted });
}

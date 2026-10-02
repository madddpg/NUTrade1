import { onSchedule } from "firebase-functions/v2/scheduler";
import * as logger from "firebase-functions/logger";
import { Timestamp } from "firebase-admin/firestore";
import { db } from "./admin";
import { LISTING_STATUS, REGION, normalizeListingPackage } from "./constants";

/**
 * Approval puts every listing on the feed immediately. The pin is decided
 * separately, and only a Priority package gets it. A client cannot choose either
 * field: Firestore rules refuse both on create.
 */
export function visibilityFor(nowMillis: number) {
  return { isVisible: true, visibleFrom: Timestamp.fromMillis(nowMillis) };
}

/**
 * Releases listings that were approved while regular posts still waited for the
 * hourly slot. New approvals are already visible; this only catches `isVisible:
 * false` rows and sets the pin from `paidPackage`.
 */
export const publishScheduledListings = onSchedule(
  { schedule: "every 5 minutes", region: REGION },
  async () => {
    const now = Timestamp.now();

    const due = await db
      .collection("listings")
      .where("status", "==", LISTING_STATUS.active)
      .where("isVisible", "==", false)
      .limit(300)
      .get();

    if (due.empty) return;

    const batch = db.batch();
    for (const doc of due.docs) {
      batch.update(doc.ref, {
        isVisible: true,
        isPinned: normalizeListingPackage(doc.get("paidPackage")) === "Priority",
        visibleFrom: now,
        becameVisibleAt: now,
      });
    }
    await batch.commit();

    logger.info("publishScheduledListings: released to the feed", { count: due.size });
  }
);

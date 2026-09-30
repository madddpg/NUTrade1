import { onSchedule } from "firebase-functions/v2/scheduler";
import * as logger from "firebase-functions/logger";
import { Timestamp } from "firebase-admin/firestore";
import { db } from "./admin";
import { LISTING_STATUS, ListingPackage, REGION, nextHourlyRefresh } from "./constants";

/**
 * "New regular listings join after the hourly refresh. Priority listings appear
 * immediately."
 *
 * Decides, at publish time, whether a listing goes straight onto the feed or waits
 * for the next top of the hour. This is the concrete thing the Priority fee buys, so
 * it is enforced server-side at the moment of publication — a client cannot choose
 * its own visibility because it cannot write these fields at all.
 */
export function visibilityFor(pkg: ListingPackage, nowMillis: number) {
  if (pkg === "Priority") {
    return { isVisible: true, visibleFrom: Timestamp.fromMillis(nowMillis) };
  }
  return { isVisible: false, visibleFrom: Timestamp.fromMillis(nextHourlyRefresh(nowMillis)) };
}

/**
 * Flips queued listings onto the feed once their slot arrives.
 *
 * Runs every 15 minutes rather than hourly on purpose: `visibleFrom` is always a top
 * of the hour, so the gate below is what decides when a listing appears, and a more
 * frequent sweep simply means a single failed run delays a listing by minutes instead
 * of by a full hour. Nothing can surface early.
 */
export const publishScheduledListings = onSchedule(
  { schedule: "every 15 minutes", region: REGION },
  async () => {
    const now = Timestamp.now();

    const due = await db
      .collection("listings")
      .where("status", "==", LISTING_STATUS.active)
      .where("isVisible", "==", false)
      .where("visibleFrom", "<=", now)
      .limit(300)
      .get();

    if (due.empty) return;

    const batch = db.batch();
    for (const doc of due.docs) {
      batch.update(doc.ref, { isVisible: true, becameVisibleAt: now });
    }
    await batch.commit();

    logger.info("publishScheduledListings: released to the feed", { count: due.size });
  }
);

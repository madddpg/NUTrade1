import { onCall, HttpsError } from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import { getAuth } from "firebase-admin/auth";
import { Timestamp } from "firebase-admin/firestore";
import { db } from "../admin";
import { BID_STATUS, LISTING_STATUS, REGION } from "../constants";

/**
 * Development seeder. Admin-only, and it writes through the Admin SDK precisely
 * the shape the real flow produces: the bid documents and denormalized listing
 * counters that placeBid maintains, and the published/pinned listings that
 * createQrPayment and the PayMongo webhook leave behind. The app therefore reads
 * genuinely live Firestore data rather than anything hard-coded client-side.
 *
 * Idempotent: listings use fixed ids and are overwritten, their bids subcollections
 * are cleared first, and anything a previous settlement produced for them (chats,
 * their messages, and trade records) is deleted — so re-running really does produce
 * the same state, even after closeExpiredAuctions has already awarded an auction.
 *
 * Never called by the app. Invoke it from scripts/seed.mjs.
 */

interface SeedAccount {
  key: string;
  email: string;
  displayName: string;
  studentId: string;
  campusHub: string;
  tradesCompleted: number;
  rating: number | null;
}

const ACCOUNTS: SeedAccount[] = [
  { key: "andrea", email: "salazarae@nu-lipa.edu.ph", displayName: "Andrea Salazar",
    studentId: "2022-104821", campusHub: "InformationTechnology", tradesCompleted: 12, rating: 4.9 },
  { key: "bea", email: "fernandezb@nu-lipa.edu.ph", displayName: "Bea Fernandez",
    studentId: "2021-098133", campusHub: "Nursing", tradesCompleted: 5, rating: 4.8 },
  { key: "jm", email: "santosjm@nu-lipa.edu.ph", displayName: "JM Santos",
    studentId: "2023-117402", campusHub: "Engineering", tradesCompleted: 3, rating: 4.6 },
  { key: "mika", email: "reyesmk@nu-lipa.edu.ph", displayName: "Mika Reyes",
    studentId: "2023-120994", campusHub: "Business", tradesCompleted: 1, rating: 5.0 },
  { key: "paolo", email: "delacruzp@nu-lipa.edu.ph", displayName: "Paolo Dela Cruz",
    studentId: "2022-101577", campusHub: "ArtsAndSciences", tradesCompleted: 7, rating: 4.7 },
];

interface SeedListing {
  id: string;
  owner: string;
  title: string;
  description: string;
  condition: string;
  category: string;
  campusZone: string;
  package: string;
  isPinned: boolean;
  startingBidCentavos: number;
  minIncrementCentavos: number;
  reservePriceCentavos: number | null;
  auctionDurationHours: number;
  /** Minutes from now until the hammer falls. Small values exercise the countdown. */
  endsInMinutes: number;
  publishedHoursAgo: number;
  /** Bid ladder: [bidder key, amount in centavos, minutes ago]. */
  bids: Array<[string, number, number]>;
}

const LISTINGS: SeedListing[] = [
  {
    id: "uniform-priority",
    owner: "jm",
    title: "NU-Lipa Uniform Set (Size M)",
    description:
      "Complete polo and slacks set, worn a handful of times. No stains or tears. Freshly laundered and ready for pickup at the Main Library.",
    condition: "LikeNew",
    category: "Uniforms",
    campusZone: "MainLibrary",
    package: "Priority",
    isPinned: true,
    startingBidCentavos: 30000,
    minIncrementCentavos: 5000,
    reservePriceCentavos: null,
    auctionDurationHours: 24,
    endsInMinutes: 38,
    publishedHoursAgo: 10,
    bids: [["mika", 30000, 300], ["paolo", 35000, 240], ["mika", 40000, 120], ["paolo", 45000, 12]],
  },
  {
    id: "uniform-basic",
    owner: "jm",
    title: "NU-Lipa Uniform Set (Size S)",
    description:
      "Barely used PE uniform, great condition. Outgrew it after first year, hoping it goes to someone who needs it.",
    condition: "LikeNew",
    category: "Uniforms",
    campusZone: "StudentHub",
    package: "Free",
    isPinned: false,
    startingBidCentavos: 30000,
    minIncrementCentavos: 5000,
    reservePriceCentavos: null,
    auctionDurationHours: 24,
    endsInMinutes: 41,
    publishedHoursAgo: 9,
    bids: [["andrea", 30000, 180], ["mika", 35000, 90], ["andrea", 45000, 20]],
  },
  {
    id: "nursing-bundle",
    owner: "bea",
    title: "Fundamentals of Nursing (Bundle of 3)",
    description:
      "Set of 3 core nursing textbooks, 2023 editions. Minimal highlighting, no missing pages. Selling as a bundle only.",
    condition: "Good",
    category: "Textbooks",
    campusZone: "MainLibrary",
    package: "Additional",
    isPinned: false,
    startingBidCentavos: 35000,
    minIncrementCentavos: 1000,
    reservePriceCentavos: 40000,
    auctionDurationHours: 48,
    endsInMinutes: 32,
    publishedHoursAgo: 20,
    bids: [["paolo", 35000, 40], ["mika", 38000, 30], ["paolo", 43000, 20], ["mika", 48000, 2]],
  },
  {
    id: "varsity-jacket",
    owner: "andrea",
    title: "NU-Lipa Varsity Jacket",
    description:
      "Official varsity jacket, size L. Selling since it no longer fits. Zipper and embroidery are both perfect.",
    condition: "New",
    category: "Other",
    campusZone: "Gymnasium",
    package: "Priority",
    isPinned: true,
    startingBidCentavos: 80000,
    minIncrementCentavos: 10000,
    reservePriceCentavos: null,
    auctionDurationHours: 72,
    endsInMinutes: 1200,
    publishedHoursAgo: 4,
    bids: [["paolo", 90000, 14], ["mika", 100000, 12]],
  },
];

export const seedDevData = onCall({ region: REGION, timeoutSeconds: 300 }, async (request) => {
  const auth = request.auth;
  if (!auth) throw new HttpsError("unauthenticated", "Sign in required.");
  if (auth.token.role !== "admin") throw new HttpsError("permission-denied", "Admins only.");

  const { password, endsInMinutes } = (request.data ?? {}) as {
    password?: string;
    endsInMinutes?: number;
  };
  if (!password || password.length < 8) {
    throw new HttpsError(
      "invalid-argument",
      "A password of at least 8 characters is required. It becomes the sign-in password for every seeded account."
    );
  }

  // Overrides every listing's auction length. A negative value seeds auctions that have
  // already ended, which is the only practical way to watch closeExpiredAuctions settle
  // one without waiting out a real countdown.
  const endsOverride =
    typeof endsInMinutes === "number" && Number.isFinite(endsInMinutes) ? endsInMinutes : null;

  const now = Timestamp.now();
  const uids: Record<string, string> = {};

  // 1. Real Auth accounts, so you can actually sign in as any seeded student.
  for (const account of ACCOUNTS) {
    let uid: string;
    try {
      uid = (await getAuth().getUserByEmail(account.email)).uid;
      await getAuth().updateUser(uid, { password, displayName: account.displayName });
    } catch {
      uid = (
        await getAuth().createUser({
          email: account.email,
          password,
          displayName: account.displayName,
          emailVerified: true,
        })
      ).uid;
    }
    // Seeded students are pre-verified; a real signup waits on admin ID review.
    await getAuth().setCustomUserClaims(uid, { verified: true });
    uids[account.key] = uid;

    await db.collection("users").doc(uid).set({
      uid,
      email: account.email,
      displayName: account.displayName,
      studentId: account.studentId,
      campusHub: account.campusHub,
      photoUrl: null,
      tradesCompleted: account.tradesCompleted,
      rating: account.rating,
      role: "student",
      verificationStatus: "verified",
      verifiedAt: now,
      fcmTokens: [],
      createdAt: Timestamp.fromMillis(now.toMillis() - 180 * 86_400_000),
    });
  }

  // 2. Listings, in exactly the shape createQrPayment and the webhook leave them.
  for (const spec of LISTINGS) {
    const listingRef = db.collection("listings").doc(spec.id);

    const existingBids = await listingRef.collection("bids").get();
    await Promise.all(existingBids.docs.map((d) => d.ref.delete()));

    // Re-seeding puts the listing back to `active`, so anything a previous
    // settlement produced for it has to go too. Without this, every re-seed of an
    // auction that closeExpiredAuctions had already awarded leaves an orphaned chat
    // behind and the same item shows up repeatedly in the chat list.
    const staleChats = await db.collection("chats").where("listingId", "==", spec.id).get();
    for (const chatDoc of staleChats.docs) {
      const messages = await chatDoc.ref.collection("messages").get();
      await Promise.all(messages.docs.map((m) => m.ref.delete()));
      await chatDoc.ref.delete();
    }

    const staleTrades = await db.collection("trades").where("listingId", "==", spec.id).get();
    await Promise.all(staleTrades.docs.map((t) => t.ref.delete()));

    const ladder = [...spec.bids].sort((a, b) => a[1] - b[1]);
    const top = ladder[ladder.length - 1];
    const publishedAt = Timestamp.fromMillis(now.toMillis() - spec.publishedHoursAgo * 3_600_000);

    await listingRef.set({
      ownerUid: uids[spec.owner],
      title: spec.title,
      description: spec.description,
      condition: spec.condition,
      category: spec.category,
      photos: [],
      campusZone: spec.campusZone,
      package: spec.package,
      isPinned: spec.isPinned,
      status: LISTING_STATUS.active,
      // Seeded auctions were published hours ago, so their hourly slot has long
      // passed — they belong on the feed immediately whatever their package.
      isVisible: true,
      visibleFrom: publishedAt,
      startingBidCentavos: spec.startingBidCentavos,
      minIncrementCentavos: spec.minIncrementCentavos,
      reservePriceCentavos: spec.reservePriceCentavos,
      currentHighestBidCentavos: top ? top[1] : spec.startingBidCentavos,
      highestBidderUid: top ? uids[top[0]] : null,
      bidCount: ladder.length,
      auctionEndsAt: Timestamp.fromMillis(
        now.toMillis() + (endsOverride ?? spec.endsInMinutes) * 60_000
      ),
      winningBidId: null,
      createdAt: publishedAt,
      publishedAt,
    });

    // Only the highest bid is pending; everything under it is outbid. Same
    // invariant placeBid maintains.
    const batch = db.batch();
    ladder.forEach(([bidderKey, amount, minutesAgo], index) => {
      const isTop = index === ladder.length - 1;
      batch.set(listingRef.collection("bids").doc(), {
        listingId: spec.id,
        bidderUid: uids[bidderKey],
        bidderName: ACCOUNTS.find((a) => a.key === bidderKey)!.displayName,
        amountCentavos: amount,
        status: isTop ? BID_STATUS.pending : BID_STATUS.outbid,
        createdAt: Timestamp.fromMillis(now.toMillis() - minutesAgo * 60_000),
      });
    });
    await batch.commit();
  }

  logger.info("seedDevData: complete", { accounts: ACCOUNTS.length, listings: LISTINGS.length });

  return {
    accounts: ACCOUNTS.map((a) => ({ email: a.email, uid: uids[a.key], displayName: a.displayName })),
    listings: LISTINGS.map((l) => l.id),
  };
});

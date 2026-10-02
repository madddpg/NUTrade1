import { onCall, HttpsError } from "firebase-functions/v2/https";
import { onSchedule } from "firebase-functions/v2/scheduler";
import * as logger from "firebase-functions/logger";
import { DocumentReference, Timestamp, Transaction } from "firebase-admin/firestore";
import { db } from "./admin";
import { PayMongoClient, mintQrPhPayment, paymentUnavailable, paymongoTestMode } from "./paymongo";
import { refreshQrOnDoc } from "./qrRefresh";
import { paymongoSecretKey } from "./createQrPayment";
import { checkIntentWithPayMongo } from "./listingFees";
import { resolveDeposit } from "./depositResolution";
import { LEDGER_KIND, postToLedger, readBalance } from "./wallet";
import {
  BID_DEPOSIT_PERCENT,
  BID_STATUS,
  DEPOSIT_INTENT_EXPIRY_MINUTES,
  DEPOSIT_STATUS,
  LISTING_KIND,
  LISTING_STATUS,
  REFUND_REASON,
  REGION,
  bidCreditSplit,
  depositFor,
  listingKind,
} from "./constants";

/**
 * Anti-ghosting deposits.
 *
 * A bid is not a bid until money is behind it. `requestBid` validates the amount and mints
 * a QR Ph code; the bid itself is written only once the deposit is confirmed paid. Nothing
 * a client can say places a bid.
 *
 * The deposit is a bond against ghost bidding, not part of the price. On a ₱500 winning
 * bid the buyer still pays ₱500 in person. The ₱75 bond comes back as bid credit when
 * they show up, lose, or are outbid. If they win and never show, that credit goes to
 * the seller. Credit pays the next deposit automatically and cannot be cashed out.
 *
 * **Returns are bid credit, not a PayMongo refund.** QR Ph captures immediately, a
 * refund takes days and costs a fee, and PayMongo cannot refund QR Ph paid through Maya.
 *
 * `bidIntents/{id}` *is* the deposit record for its whole life — it is not thrown away
 * once the bid exists, because the deposit outlives the bid.
 */

/** A deposit that has been paid and is being held against a live bid. */
const HELD = DEPOSIT_STATUS.lockedInEscrow;

interface RequestBidData {
  listingId: string;
  amountCentavos: number;
}

function intentRefFor(id: string): DocumentReference {
  return db.collection("bidIntents").doc(id);
}

/**
 * Step 1: validate the bid and mint the deposit QR. Writes no bid — see `commitDeposit`.
 */
export const requestBid = onCall(
  { secrets: [paymongoSecretKey], region: REGION },
  async (request) => {
    const auth = request.auth;
    if (!auth) throw new HttpsError("unauthenticated", "Sign in required.");
    if (auth.token.verified !== true) {
      throw new HttpsError("permission-denied", "Confirm your email before bidding.");
    }

    const { listingId, amountCentavos } = (request.data ?? {}) as Partial<RequestBidData>;
    if (!listingId || typeof listingId !== "string") {
      throw new HttpsError("invalid-argument", "listingId is required.");
    }
    if (
      typeof amountCentavos !== "number" ||
      !Number.isSafeInteger(amountCentavos) ||
      amountCentavos <= 0
    ) {
      throw new HttpsError("invalid-argument", "amountCentavos must be a positive whole number.");
    }

    const listingSnap = await db.collection("listings").doc(listingId).get();
    if (!listingSnap.exists) throw new HttpsError("not-found", "Listing not found.");
    const listing = listingSnap.data()!;

    if (listing.ownerUid === auth.uid) {
      throw new HttpsError("failed-precondition", "You can't bid on your own listing.");
    }
    if (listingKind(listing) !== LISTING_KIND.auction) {
      throw new HttpsError("failed-precondition", "Only auctions take bids.");
    }
    if (listing.status !== LISTING_STATUS.active) {
      throw new HttpsError("failed-precondition", "This auction isn't accepting bids.");
    }

    const now = Timestamp.now();
    const endsAt = listing.auctionEndsAt as Timestamp | undefined;
    if (endsAt && endsAt.toMillis() <= now.toMillis()) {
      throw new HttpsError("failed-precondition", "This auction has already ended.");
    }

    // Checked here so a student is never asked to pay a deposit for a bid that was never
    // going to be accepted. Checked *again* when the deposit lands, because the auction
    // moves while the QR is unscanned.
    assertBidBeatsTheField(listing, amountCentavos);

    const depositCentavos = depositFor(amountCentavos);

    const bidderSnap = await db.collection("users").doc(auth.uid).get();
    const bidder = bidderSnap.data() ?? {};
    const bidderName = (bidder.displayName as string) ?? "NU student";

    const intentRef = db.collection("bidIntents").doc();
    const listingRef = db.collection("listings").doc(listingId);

    const reserved = await db.runTransaction(async (tx) => {
      const freshSnap = await tx.get(listingRef);
      if (!freshSnap.exists) throw new HttpsError("not-found", "Listing not found.");
      const fresh = freshSnap.data()!;
      if (fresh.ownerUid === auth.uid) {
        throw new HttpsError("failed-precondition", "You can't bid on your own listing.");
      }
      if (listingKind(fresh) !== LISTING_KIND.auction || fresh.status !== LISTING_STATUS.active) {
        throw new HttpsError("failed-precondition", "This auction isn't accepting bids.");
      }
      const freshEnds = fresh.auctionEndsAt as Timestamp | undefined;
      if (freshEnds && freshEnds.toMillis() <= now.toMillis()) {
        throw new HttpsError("failed-precondition", "This auction has already ended.");
      }
      assertBidBeatsTheField(fresh, amountCentavos);

      const balance = await readBalance(auth.uid, tx);
      const { applied, qrDue } = bidCreditSplit(balance, depositCentavos);

      const leaders = qrDue === 0
        ? await tx.get(listingRef.collection("bids").where("status", "==", BID_STATUS.pending))
        : null;
      const leaderDeposits: Array<{ ref: DocumentReference; data: FirebaseFirestore.DocumentData }> = [];
      if (leaders) {
        for (const doc of leaders.docs) {
          const depositId = doc.data()["depositIntentId"] as string | undefined;
          if (!depositId) continue;
          const ref = intentRefFor(depositId);
          const snap = await tx.get(ref);
          if (snap.exists) leaderDeposits.push({ ref, data: snap.data()! });
        }
      }

      if (applied > 0) {
        postToLedger(tx, auth.uid, -applied, LEDGER_KIND.bidCreditSpent, {
          listingId,
          note: qrDue === 0 ? "Bid credit covered the deposit" : "Bid credit applied to the deposit",
        });
      }

      const intent = {
        listingId,
        listingTitle: fresh.title ?? "",
        sellerUid: fresh.ownerUid,
        bidderUid: auth.uid,
        bidderName,
        amountCentavos,
        depositCentavos,
        depositPercent: BID_DEPOSIT_PERCENT,
        creditAppliedCentavos: applied,
        qrDueCentavos: qrDue,
        creditReleased: false,
        paymongoIntentId: null,
        qrImageUrl: null,
        qrImageBase64: null,
        qrPayload: null,
        qrExpiresAt: null,
        expiresAt: Timestamp.fromMillis(now.toMillis() + DEPOSIT_INTENT_EXPIRY_MINUTES * 60_000),
        createdAt: now,
        resolvedAt: null,
        refundReason: null,
        refundedVia: null,
        committedBidId: null,
      };

      if (qrDue === 0) {
        const bidId = writeLiveBid(tx, intentRef, intent, listingRef, fresh, leaders!, leaderDeposits, now, "credit");
        return { applied, qrDue, committed: true, bidId };
      }

      tx.set(intentRef, { ...intent, status: DEPOSIT_STATUS.awaitingPayment, paidAt: null, settledBy: null });
      return { applied, qrDue, committed: false, bidId: null as string | null };
    });

    if (reserved.committed) {
      logger.info("requestBid: covered by bid credit", { bidIntentId: intentRef.id, listingId });
      return {
        bidIntentId: intentRef.id,
        amountCentavos,
        depositCentavos,
        depositPercent: BID_DEPOSIT_PERCENT,
        creditAppliedCentavos: reserved.applied,
        qrDueCentavos: 0,
        coveredByCredit: true,
        status: HELD,
        bidId: reserved.bidId,
        qrImageUrl: null,
        qrImageBase64: null,
        qrPayload: null,
        expiresAt: null,
      };
    }

    const client = new PayMongoClient(paymongoSecretKey.value());
    let minted;
    try {
      minted = await mintQrPhPayment(client, {
        amountCentavos: reserved.qrDue,
        description: `NUTrade bid deposit — ${listing.title} (₱${(amountCentavos / 100).toFixed(2)})`,
        billingName: (bidder.displayName as string) ?? "NUTrade student",
        billingEmail: (bidder.email as string) ?? auth.token.email ?? "",
      });
    } catch (err) {
      await releaseReservedCredit(intentRef, "Couldn't start the QR — bid credit returned");
      throw paymentUnavailable(err);
    }
    const { intentId, qr } = minted;
    const qrExpiresAt = qrExpiresAtTimestamp(qr.expiresAt, now);
    const testMode = paymongoTestMode(paymongoSecretKey.value());
    await intentRef.update({
      paymongoIntentId: intentId,
      qrImageUrl: qr.qrImageUrl ?? null,
      qrImageBase64: qr.qrImageBase64 ?? null,
      qrPayload: qr.qrPayload ?? null,
      qrExpiresAt,
      paymongoTestMode: testMode,
    });

    logger.info("requestBid: deposit QR issued", {
      bidIntentId: intentRef.id,
      listingId,
      creditAppliedCentavos: reserved.applied,
      qrDueCentavos: reserved.qrDue,
    });

    return {
      bidIntentId: intentRef.id,
      amountCentavos,
      depositCentavos,
      depositPercent: BID_DEPOSIT_PERCENT,
      creditAppliedCentavos: reserved.applied,
      qrDueCentavos: reserved.qrDue,
      coveredByCredit: false,
      status: DEPOSIT_STATUS.awaitingPayment,
      bidId: null,
      qrImageUrl: qr.qrImageUrl ?? null,
      qrImageBase64: qr.qrImageBase64 ?? null,
      qrPayload: qr.qrPayload ?? null,
      expiresAt: qrExpiresAt.toMillis(),
      testMode,
    };
  }
);

/** Writes the live bid and returns the previous leader's bond. Reads must already be done. */
function writeLiveBid(
  tx: Transaction,
  intentRef: DocumentReference,
  intent: FirebaseFirestore.DocumentData,
  listingRef: DocumentReference,
  listing: FirebaseFirestore.DocumentData,
  leaders: FirebaseFirestore.QuerySnapshot,
  leaderDeposits: Array<{ ref: DocumentReference; data: FirebaseFirestore.DocumentData }>,
  now: Timestamp,
  source: string
): string {
  const amount = intent.amountCentavos as number;
  const bidRef = listingRef.collection("bids").doc();
  tx.set(bidRef, {
    listingId: intent.listingId,
    bidderUid: intent.bidderUid,
    bidderName: intent.bidderName ?? "NU student",
    amountCentavos: amount,
    depositCentavos: intent.depositCentavos,
    depositIntentId: intentRef.id,
    status: BID_STATUS.pending,
    createdAt: now,
  });
  tx.set(intentRef, {
    ...intent,
    status: HELD,
    paidAt: now,
    settledBy: source,
    committedBidId: bidRef.id,
  });
  for (const doc of leaders.docs) {
    tx.update(doc.ref, { status: BID_STATUS.outbid, outbidAt: now });
  }
  for (const { ref, data } of leaderDeposits) {
    resolveDeposit(tx, ref, data, "refunded_to_buyer", { reason: REFUND_REASON.outbid });
  }
  tx.update(listingRef, {
    currentHighestBidCentavos: amount,
    highestBidderUid: intent.bidderUid,
    bidCount: ((listing.bidCount as number) ?? 0) + 1,
  });
  return bidRef.id;
}

/** Gives back credit reserved for a QR that never became a bid. */
async function releaseReservedCredit(intentRef: DocumentReference, note: string): Promise<void> {
  await db.runTransaction(async (tx) => {
    const snap = await tx.get(intentRef);
    const intent = snap.data();
    if (!intent || intent.status !== DEPOSIT_STATUS.awaitingPayment || intent.creditReleased === true) return;
    const applied = (intent.creditAppliedCentavos as number) ?? 0;
    tx.update(intentRef, {
      status: DEPOSIT_STATUS.expired,
      creditReleased: true,
      resolvedAt: Timestamp.now(),
    });
    if (applied > 0) {
      postToLedger(tx, intent.bidderUid as string, applied, LEDGER_KIND.refundCredit, {
        listingId: (intent.listingId as string) ?? null,
        note,
      });
    }
  });
}

/** PayMongo's expiry when it sent one; otherwise the bid window, so the countdown is not a guess of 10 minutes. */
function qrExpiresAtTimestamp(paymongoExpiresAt: string | undefined, now: Timestamp): Timestamp {
  const parsed = paymongoExpiresAt ? Date.parse(paymongoExpiresAt) : NaN;
  if (!Number.isNaN(parsed)) return Timestamp.fromMillis(parsed);
  return Timestamp.fromMillis(now.toMillis() + DEPOSIT_INTENT_EXPIRY_MINUTES * 60_000);
}

/** Throws unless `amount` clears the listing's current bar. */
function assertBidBeatsTheField(listing: FirebaseFirestore.DocumentData, amount: number): void {
  const bidCount = (listing.bidCount as number) ?? 0;
  const startingBid = (listing.startingBidCentavos as number) ?? 0;
  const highest = (listing.currentHighestBidCentavos as number) ?? startingBid;
  const increment = (listing.minIncrementCentavos as number) ?? 0;
  const minNext = bidCount === 0 ? startingBid : highest + increment;

  if (amount < minNext) {
    throw new HttpsError(
      "failed-precondition",
      `Your bid must be at least ₱${(minNext / 100).toFixed(2)}.`
    );
  }
}

/**
 * Step 2: the deposit has been paid, so write the bid it was raised for.
 *
 * Idempotent and race-safe — the webhook and every reconciler may call this for the same
 * intent, and only the first does anything. Returns true when this call is the one that
 * committed it.
 *
 * The bid is created here rather than in `placeBid`, and the previous leader's deposit is
 * returned in the same transaction, so a listing never has two live bids and never has two
 * deposits held at once.
 */
export async function commitDeposit(
  intentRef: DocumentReference,
  source: "webhook" | "reconcile"
): Promise<boolean> {
  const outcome = await db.runTransaction(async (tx) => {
    // Every read first — Firestore forbids a read after a write in a transaction.
    const intentSnap = await tx.get(intentRef);
    const intent = intentSnap.data();
    if (!intent || intent.status !== DEPOSIT_STATUS.awaitingPayment) return null;

    const listingRef = db.collection("listings").doc(intent.listingId as string);
    const listingSnap = await tx.get(listingRef);
    if (!listingSnap.exists) return null;
    const listing = listingSnap.data()!;

    const bidsRef = listingRef.collection("bids");
    const leaders = await tx.get(bidsRef.where("status", "==", BID_STATUS.pending));

    // The deposit each displaced leader is holding, so it can go back in this same write.
    const leaderDeposits: Array<{ ref: DocumentReference; data: FirebaseFirestore.DocumentData }> = [];
    for (const doc of leaders.docs) {
      const depositId = doc.data()["depositIntentId"] as string | undefined;
      if (!depositId) continue;
      const ref = intentRefFor(depositId);
      const snap = await tx.get(ref);
      if (snap.exists) leaderDeposits.push({ ref, data: snap.data()! });
    }

    const now = Timestamp.now();
    const amount = intent.amountCentavos as number;

    // The auction moved while the QR sat unscanned: the money arrived, but this bid can
    // no longer be placed. Take the deposit into escrow and hand it straight back, so the
    // student is never charged for a bid that does not exist.
    // The seller check repeats requestBid's on purpose: this is the one function that
    // writes a bid, so it must never write one for the listing's own seller, whatever
    // path the intent took to get here.
    const endsAt = listing.auctionEndsAt as Timestamp | undefined;
    const stale =
      intent.bidderUid === listing.ownerUid ||
      listingKind(listing) !== LISTING_KIND.auction ||
      listing.status !== LISTING_STATUS.active ||
      (endsAt !== undefined && endsAt.toMillis() <= now.toMillis()) ||
      amount < minimumNextBid(listing);

    if (stale) {
      tx.update(intentRef, { status: HELD, paidAt: now, settledBy: source });
      resolveDeposit(tx, intentRef, { ...intent, status: HELD }, "refunded_to_buyer", {
        reason: REFUND_REASON.auctionLost,
        note: "Bid no longer valid — deposit returned",
      });
      return { committed: false, bidderUid: intent.bidderUid as string };
    }

    const bidRef = bidsRef.doc();
    tx.set(bidRef, {
      listingId: intent.listingId,
      bidderUid: intent.bidderUid,
      bidderName: intent.bidderName ?? "NU student",
      amountCentavos: amount,
      depositCentavos: intent.depositCentavos,
      depositIntentId: intentRef.id,
      status: BID_STATUS.pending,
      createdAt: now,
    });

    tx.update(intentRef, {
      status: HELD,
      paidAt: now,
      settledBy: source,
      committedBidId: bidRef.id,
    });

    // Demote the old leader and return its deposit — this is the outbid path, and it runs
    // on every single bid, so it stays a wallet credit with no network call in it.
    for (const doc of leaders.docs) {
      tx.update(doc.ref, { status: BID_STATUS.outbid, outbidAt: now });
    }
    for (const { ref, data } of leaderDeposits) {
      resolveDeposit(tx, ref, data, "refunded_to_buyer", { reason: REFUND_REASON.outbid });
    }

    tx.update(listingRef, {
      currentHighestBidCentavos: amount,
      highestBidderUid: intent.bidderUid,
      bidCount: ((listing.bidCount as number) ?? 0) + 1,
    });

    return { committed: true, bidId: bidRef.id, bidderUid: intent.bidderUid as string };
  });

  if (!outcome) return false;
  logger.info("commitDeposit: settled", { intentId: intentRef.id, source, ...outcome });
  return true;
}

function minimumNextBid(listing: FirebaseFirestore.DocumentData): number {
  const bidCount = (listing.bidCount as number) ?? 0;
  const startingBid = (listing.startingBidCentavos as number) ?? 0;
  const highest = (listing.currentHighestBidCentavos as number) ?? startingBid;
  const increment = (listing.minIncrementCentavos as number) ?? 0;
  return bidCount === 0 ? startingBid : highest + increment;
}

/**
 * Asks PayMongo whether a deposit was actually paid, and commits it if so.
 *
 * This — not the webhook — is what actually places bids in this project: the PayMongo
 * webhook has never arrived here, so the app polls this while the QR screen is open. The
 * evidence is PayMongo's, fetched with our secret key; nothing the student says settles a
 * deposit.
 */
export const checkBidDeposit = onCall(
  { secrets: [paymongoSecretKey], region: REGION },
  async (request) => {
    const auth = request.auth;
    if (!auth) throw new HttpsError("unauthenticated", "Sign in required.");

    const { bidIntentId } = (request.data ?? {}) as { bidIntentId?: unknown };
    if (typeof bidIntentId !== "string" || bidIntentId.length === 0) {
      throw new HttpsError("invalid-argument", "bidIntentId is required.");
    }

    const intentRef = intentRefFor(bidIntentId);
    const snap = await intentRef.get();
    if (!snap.exists) throw new HttpsError("not-found", "No such bid.");
    const intent = snap.data()!;

    if (intent.bidderUid !== auth.uid && auth.token.role !== "admin") {
      throw new HttpsError("permission-denied", "This isn't your bid.");
    }

    if (intent.status === DEPOSIT_STATUS.awaitingPayment && intent.paymongoIntentId) {
      const client = new PayMongoClient(paymongoSecretKey.value());
      if (paymongoTestMode(paymongoSecretKey.value()) && intent.paymongoTestMode !== true) {
        await intentRef.update({ paymongoTestMode: true });
      }
      const verdict = await checkIntentWithPayMongo(client, intent.paymongoIntentId as string);
      if (verdict === "paid") {
        await commitDeposit(intentRef, "reconcile");
      } else if (verdict === "unpaid") {
        await refreshQrOnDoc(
          client,
          intentRef,
          intent,
          intent.paymongoIntentId as string,
          intent.bidderUid as string
        );
      }
    }

    const latest = (await intentRef.get()).data()!;
    return {
      bidIntentId,
      status: latest.status,
      bidId: latest.committedBidId ?? null,
      refundReason: latest.refundReason ?? null,
    };
  }
);

/**
 * Sweeps deposit QRs nobody paid. Runs often enough that a student who walked away is not
 * still holding the top-bid slot hostage — the bid was never created, so this only tidies
 * the intent.
 *
 * It asks PayMongo before giving up on each one: a QR paid in the last second of the
 * window must not be swept as unpaid.
 */
export const expireDepositIntents = onSchedule(
  { schedule: "every 5 minutes", secrets: [paymongoSecretKey], region: REGION },
  async () => {
    const now = Timestamp.now();
    const stale = await db
      .collection("bidIntents")
      .where("status", "==", DEPOSIT_STATUS.awaitingPayment)
      .where("expiresAt", "<=", now)
      .limit(100)
      .get();

    if (stale.empty) return;

    const client = new PayMongoClient(paymongoSecretKey.value());
    let expired = 0;
    let rescued = 0;

    for (const doc of stale.docs) {
      const intentId = doc.data().paymongoIntentId as string | undefined;
      const verdict = intentId ? await checkIntentWithPayMongo(client, intentId) : "unpaid";

      if (verdict === "paid") {
        await commitDeposit(doc.ref, "reconcile");
        rescued++;
        continue;
      }
      // "unknown" means the lookup failed, not that it went unpaid — leave it for the
      // next sweep rather than expiring a deposit that may have landed.
      if (verdict === "unpaid") {
        await releaseReservedCredit(doc.ref, "Deposit code expired — bid credit returned");
        expired++;
      }
    }

    logger.info("expireDepositIntents: swept", { checked: stale.size, expired, rescued });
  }
);

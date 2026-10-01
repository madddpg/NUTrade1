import { onCall, HttpsError } from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import { DocumentData, DocumentReference, FieldValue, Timestamp } from "firebase-admin/firestore";
import { db } from "./admin";
import { readDepositForBid, resolveDeposit } from "./depositResolution";
import { BID_STATUS, DISPUTE_STATUS, LISTING_STATUS, REFUND_REASON, REGION } from "./constants";

/**
 * The two ways a matched trade ends badly.
 *
 * A confirmed handover is the happy path and lives in `markTradeCompleted`. This file
 * covers the rest: the seller pulling out, and the winning bidder not turning up.
 *
 * The asymmetry is deliberate. A seller cancelling is their own decision, so the buyer is
 * made whole immediately and automatically. A buyer failing to appear is one person's word
 * against another's, so it opens a **dispute** an admin settles — a seller who could
 * forfeit a deposit by pressing a button could simply steal it.
 */

/** Reads the chat, the winning bid and its deposit. Call before writing anything. */
async function readTrade(
  tx: FirebaseFirestore.Transaction,
  chatRef: DocumentReference
): Promise<{
  chat: DocumentData;
  bidRef: DocumentReference | null;
  deposit: { ref: DocumentReference; data: DocumentData } | null;
}> {
  const chatSnap = await tx.get(chatRef);
  if (!chatSnap.exists) throw new HttpsError("not-found", "Trade not found.");
  const chat = chatSnap.data()!;

  const winningBidId = chat.winningBidId as string | undefined;
  if (!winningBidId) return { chat, bidRef: null, deposit: null };

  const bidRef = db
    .collection("listings")
    .doc(chat.listingId as string)
    .collection("bids")
    .doc(winningBidId);
  const bidSnap = await tx.get(bidRef);
  if (!bidSnap.exists) return { chat, bidRef: null, deposit: null };

  return { chat, bidRef, deposit: await readDepositForBid(tx, bidSnap.data()!) };
}

function assertOpen(chat: DocumentData): void {
  if (chat.status === "archived") {
    throw new HttpsError("failed-precondition", "This trade is already closed.");
  }
}

/**
 * The seller pulls out after a deposit has been paid.
 *
 * The buyer gets nothing from the trade, so the bond comes back as bid credit. It is not
 * sent back to GCash: that refund is slow, costs a fee, and fails outright for Maya.
 */
export const cancelTrade = onCall({ region: REGION }, async (request) => {
    const auth = request.auth;
    if (!auth) throw new HttpsError("unauthenticated", "Sign in required.");

    const data = request.data ?? {};
    const chatId = typeof data.chatId === "string" ? data.chatId : "";
    if (!chatId) throw new HttpsError("invalid-argument", "chatId is required.");
    const reason = typeof data.reason === "string" ? data.reason.trim().slice(0, 300) : "";

    const chatRef = db.collection("chats").doc(chatId);

    // Peek so a stranger is refused before the transaction opens.
    const peek = await chatRef.get();
    if (!peek.exists) throw new HttpsError("not-found", "Trade not found.");
    const peeked = peek.data()!;
    if (peeked.sellerUid !== auth.uid) {
      throw new HttpsError("permission-denied", "Only the seller can cancel a trade.");
    }
    assertOpen(peeked);

    const outcome = await db.runTransaction(async (tx) => {
      const { chat, bidRef, deposit } = await readTrade(tx, chatRef);
      if (chat.sellerUid !== auth.uid) {
        throw new HttpsError("permission-denied", "Only the seller can cancel a trade.");
      }
      assertOpen(chat);

      const now = Timestamp.now();

      if (deposit) {
        resolveDeposit(tx, deposit.ref, deposit.data, "refunded_to_buyer", {
          reason: REFUND_REASON.sellerCancelled,
          via: "wallet_credit",
          note: "Seller cancelled — deposit back as bid credit",
        });
      }
      if (bidRef) tx.update(bidRef, { status: BID_STATUS.declined, declinedAt: now });

      tx.update(chatRef, { status: "archived", cancelledAt: now, cancelledBy: auth.uid });
      tx.set(chatRef.collection("messages").doc(), {
        senderUid: "system",
        text: reason
          ? `The seller cancelled this trade: ${reason}`
          : "The seller cancelled this trade. Your deposit is back as bid credit.",
        type: "system",
        sentAt: now,
      });
      tx.update(db.collection("listings").doc(chat.listingId as string), {
        status: LISTING_STATUS.cancelled,
        cancelledAt: now,
      });

      return { buyerUid: chat.buyerUid as string };
    });

    logger.info("cancelTrade", { chatId, seller: auth.uid, ...outcome });
    return { ok: true, refundedVia: "wallet_credit" };
  }
);

/**
 * The seller says the winning bidder never turned up.
 *
 * This forfeits nothing by itself. It opens a dispute for an admin, because the deposit is
 * the buyer's money and one party should not be able to take it on their own say-so.
 */
export const reportNoShow = onCall({ region: REGION }, async (request) => {
  const auth = request.auth;
  if (!auth) throw new HttpsError("unauthenticated", "Sign in required.");

  const data = request.data ?? {};
  const chatId = typeof data.chatId === "string" ? data.chatId : "";
  if (!chatId) throw new HttpsError("invalid-argument", "chatId is required.");
  const note = typeof data.note === "string" ? data.note.trim().slice(0, 300) : "";

  const chatRef = db.collection("chats").doc(chatId);
  const disputeRef = db.collection("disputes").doc();

  await db.runTransaction(async (tx) => {
    const { chat, deposit } = await readTrade(tx, chatRef);
    if (chat.sellerUid !== auth.uid) {
      throw new HttpsError("permission-denied", "Only the seller can report a no-show.");
    }
    assertOpen(chat);
    if (chat.disputeId) {
      throw new HttpsError("failed-precondition", "A dispute is already open on this trade.");
    }
    if (!deposit) {
      throw new HttpsError("failed-precondition", "There is no deposit on this trade to dispute.");
    }

    const now = Timestamp.now();
    tx.set(disputeRef, {
      chatId,
      listingId: chat.listingId,
      listingTitle: chat.listingTitle ?? "",
      sellerUid: chat.sellerUid,
      buyerUid: chat.buyerUid,
      depositIntentId: deposit.ref.id,
      depositCentavos: deposit.data.depositCentavos,
      note: note || null,
      status: DISPUTE_STATUS.open,
      createdAt: now,
      resolvedAt: null,
      resolvedBy: null,
      resolution: null,
    });
    tx.update(chatRef, { disputeId: disputeRef.id, disputedAt: now });
    tx.set(chatRef.collection("messages").doc(), {
      senderUid: "system",
      text: "The seller reported a no-show. A NUTrade admin will review this.",
      type: "system",
      sentAt: now,
    });
  });

  logger.info("reportNoShow: dispute opened", { chatId, disputeId: disputeRef.id });
  return { disputeId: disputeRef.id };
});

/**
 * An admin settles a no-show dispute.
 *
 * `forfeit` gives the bidder's deposit to the seller as bid credit and records a strike.
 * A pattern of strikes is what an admin acts on, not one bad day.
 * `refund` returns the deposit to the bidder as bid credit and leaves no mark.
 */
export const resolveDispute = onCall({ region: REGION }, async (request) => {
  const auth = request.auth;
  if (!auth) throw new HttpsError("unauthenticated", "Sign in required.");
  if (auth.token.role !== "admin") throw new HttpsError("permission-denied", "Admins only.");

  const data = request.data ?? {};
  const disputeId = typeof data.disputeId === "string" ? data.disputeId : "";
  const resolution = data.resolution === "forfeit" ? "forfeit" : data.resolution === "refund" ? "refund" : null;
  if (!disputeId || !resolution) {
    throw new HttpsError("invalid-argument", "disputeId and a resolution of forfeit or refund are required.");
  }

  const disputeRef = db.collection("disputes").doc(disputeId);

  const outcome = await db.runTransaction(async (tx) => {
    const disputeSnap = await tx.get(disputeRef);
    const dispute = disputeSnap.data();
    if (!dispute) throw new HttpsError("not-found", "No such dispute.");
    if (dispute.status !== DISPUTE_STATUS.open) {
      throw new HttpsError("failed-precondition", "That dispute has already been resolved.");
    }

    const depositRef = db.collection("bidIntents").doc(dispute.depositIntentId as string);
    const depositSnap = await tx.get(depositRef);
    const deposit = depositSnap.data();
    if (!deposit) throw new HttpsError("not-found", "The deposit for this dispute is missing.");

    const now = Timestamp.now();
    const buyerUid = dispute.buyerUid as string;

    if (resolution === "forfeit") {
      resolveDeposit(tx, depositRef, deposit, "forfeited", { reason: "no_show" });
      tx.set(
        db.collection("users").doc(buyerUid),
        { strikes: FieldValue.increment(1), lastStrikeAt: now },
        { merge: true }
      );
    } else {
      resolveDeposit(tx, depositRef, deposit, "refunded_to_buyer", {
        reason: "no_show_dismissed",
        note: "Dispute resolved in your favour — deposit returned",
      });
    }

    tx.update(disputeRef, {
      status: DISPUTE_STATUS.resolved,
      resolution,
      resolvedAt: now,
      resolvedBy: auth.uid,
    });
    tx.update(db.collection("chats").doc(dispute.chatId as string), {
      status: "archived",
      completedAt: now,
    });

    return { buyerUid, resolution };
  });

  logger.info("resolveDispute", { disputeId, admin: auth.uid, ...outcome });
  return { disputeId, resolution };
});

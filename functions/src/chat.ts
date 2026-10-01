import { onCall, HttpsError } from "firebase-functions/v2/https";
import { onDocumentCreated } from "firebase-functions/v2/firestore";
import * as logger from "firebase-functions/logger";
import { FieldValue, Timestamp } from "firebase-admin/firestore";
import { db } from "./admin";
import { readDepositForBid, resolveDeposit } from "./depositResolution";
import { LISTING_STATUS, REGION } from "./constants";

/**
 * Keeps `chats/{chatId}.lastMessage` / `lastMessageAt` current for the chat-list
 * preview row. The chat document itself is server-only in firestore.rules, so the
 * client writing its own message can't maintain this — a trigger does it instead.
 */
export const onChatMessageCreated = onDocumentCreated(
  { document: "chats/{chatId}/messages/{messageId}", region: REGION },
  async (event) => {
    const message = event.data?.data();
    if (!message) return;

    await db.collection("chats").doc(event.params.chatId).update({
      lastMessage: (message.text as string) ?? "",
      lastMessageAt: (message.sentAt as Timestamp) ?? Timestamp.now(),
    });
  }
);

/**
 * One participant confirms the handover happened. The trade only closes out when
 * both sides have — a single tap from the buyer must not be able to credit the
 * seller with a completed trade that never took place.
 */
export const markTradeCompleted = onCall({ region: REGION }, async (request) => {
  const auth = request.auth;
  if (!auth) throw new HttpsError("unauthenticated", "Sign in required.");

  const { chatId } = (request.data ?? {}) as { chatId?: string };
  if (!chatId) throw new HttpsError("invalid-argument", "chatId is required.");

  const chatRef = db.collection("chats").doc(chatId);

  const result = await db.runTransaction(async (tx) => {
    const chatSnap = await tx.get(chatRef);
    if (!chatSnap.exists) throw new HttpsError("not-found", "Chat not found.");
    const chat = chatSnap.data()!;

    const participants = (chat.participantUids as string[]) ?? [];
    if (!participants.includes(auth.uid)) {
      throw new HttpsError("permission-denied", "You're not in this chat.");
    }

    // Read the winning bid's deposit now: Firestore forbids a read after a write, and
    // the both-confirmed branch below has to resolve it.
    const winningBidId = chat.winningBidId as string | undefined;
    let deposit: { ref: FirebaseFirestore.DocumentReference; data: FirebaseFirestore.DocumentData } | null = null;
    if (winningBidId) {
      const bidRef = db
        .collection("listings")
        .doc(chat.listingId as string)
        .collection("bids")
        .doc(winningBidId);
      const bidSnap = await tx.get(bidRef);
      if (bidSnap.exists) deposit = await readDepositForBid(tx, bidSnap.data()!);
    }

    const completedBy = new Set<string>((chat.completedBy as string[]) ?? []);
    if (completedBy.has(auth.uid)) {
      return { bothConfirmed: chat.status === "archived", alreadyConfirmed: true };
    }
    completedBy.add(auth.uid);

    const now = Timestamp.now();
    const bothConfirmed = participants.every((uid) => completedBy.has(uid));

    if (!bothConfirmed) {
      tx.update(chatRef, { completedBy: [...completedBy] });
      tx.set(chatRef.collection("messages").doc(), {
        senderUid: "system",
        text: "One side marked this trade completed. Waiting on the other.",
        type: "system",
        sentAt: now,
      });
      return { bothConfirmed: false, alreadyConfirmed: false };
    }

    const sellerUid = chat.sellerUid as string;
    const buyerUid = chat.buyerUid as string;

    // The bond did its job. The buyer pays the full price in person, and the deposit
    // comes back to them as bid credit. It is not part of the seller's payment.
    if (deposit) {
      resolveDeposit(tx, deposit.ref, deposit.data, "refunded_to_buyer", {
        reason: "trade_completed",
        note: "You showed up — deposit back as bid credit",
      });
    }

    tx.update(chatRef, { completedBy: [...completedBy], status: "archived", completedAt: now });
    tx.update(db.collection("listings").doc(chat.listingId as string), {
      status: LISTING_STATUS.completed,
      completedAt: now,
    });
    tx.set(db.collection("trades").doc(), {
      chatId,
      listingId: chat.listingId,
      listingTitle: chat.listingTitle ?? "",
      sellerUid,
      buyerUid,
      participantUids: participants,
      amountCentavos: chat.winningBidCentavos ?? 0,
      completedAt: now,
    });
    for (const uid of [sellerUid, buyerUid]) {
      tx.update(db.collection("users").doc(uid), {
        tradesCompleted: FieldValue.increment(1),
      });
    }
    tx.set(chatRef.collection("messages").doc(), {
      senderUid: "system",
      text: "Trade completed. The full price was paid in person, and the buyer's deposit is back as bid credit.",
      type: "system",
      sentAt: now,
    });

    return { bothConfirmed: true, alreadyConfirmed: false };
  });

  logger.info("markTradeCompleted", { chatId, uid: auth.uid, ...result });
  return result;
});

import * as logger from "firebase-functions/logger";
import { FieldValue } from "firebase-admin/firestore";
import { getMessaging } from "firebase-admin/messaging";
import { db } from "./admin";

/**
 * Best-effort push notification — the state it announces is already committed by
 * the time this runs, so a messaging failure (bad/stale token, client not yet
 * wired for FCM) must never fail the caller. Reads `users/{uid}.fcmTokens`
 * (string[]), which nothing currently populates client-side; sends silently
 * no-op until that registration ships.
 */
export async function notifyUser(
  uid: string,
  message: { title: string; body: string; data: Record<string, string> }
): Promise<void> {
  try {
    const userSnap = await db.collection("users").doc(uid).get();
    const tokens = (userSnap.data()?.["fcmTokens"] as string[] | undefined) ?? [];
    if (tokens.length === 0) return;

    const response = await getMessaging().sendEachForMulticast({
      tokens,
      notification: { title: message.title, body: message.body },
      data: message.data,
    });

    const staleTokens = tokens.filter((_, i) => {
      const err = response.responses[i].error;
      return err?.code === "messaging/registration-token-not-registered";
    });
    if (staleTokens.length > 0) {
      await db.collection("users").doc(uid).update({
        fcmTokens: FieldValue.arrayRemove(...staleTokens),
      });
    }
  } catch (err) {
    logger.warn("notifyUser: failed, ignoring", { uid, data: message.data, err });
  }
}

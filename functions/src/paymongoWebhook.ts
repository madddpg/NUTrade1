import * as crypto from "crypto";
import { onRequest } from "firebase-functions/v2/https";
import { defineSecret } from "firebase-functions/params";
import * as logger from "firebase-functions/logger";
import { db } from "./admin";
import { paymongoSecretKey } from "./createQrPayment";
import { checkIntentWithPayMongo, reconcilePayment } from "./listingFees";
import { settleOrderPaid } from "./orders";
import { commitDeposit } from "./deposits";
import { PayMongoClient } from "./paymongo";
import { refreshQrOnDoc } from "./qrRefresh";

export const paymongoWebhookSecret = defineSecret("PAYMONGO_WEBHOOK_SECRET");

/**
 * Verifies PayMongo's `Paymongo-Signature` header: `t=<unix ts>,te=<test hmac>,li=<live hmac>`,
 * where the hmac is HMAC-SHA256 over `${t}.${rawBody}` keyed by the webhook's signing secret.
 * We're a live-mode endpoint, so we check the `li` component. Uses a constant-time compare.
 */
function isValidSignature(rawBody: Buffer, signatureHeader: string, secret: string): boolean {
  const parts = Object.fromEntries(
    signatureHeader.split(",").map((kv) => {
      const [k, v] = kv.split("=");
      return [k?.trim(), v?.trim()];
    })
  );

  const timestamp = parts["t"];
  const liveSignature = parts["li"];
  if (!timestamp || !liveSignature) return false;

  const signedPayload = `${timestamp}.${rawBody.toString("utf8")}`;
  const expected = crypto.createHmac("sha256", secret).update(signedPayload).digest("hex");

  const expectedBuf = Buffer.from(expected, "utf8");
  const actualBuf = Buffer.from(liveSignature, "utf8");
  if (expectedBuf.length !== actualBuf.length) return false;
  return crypto.timingSafeEqual(expectedBuf, actualBuf);
}

export const paymongoWebhook = onRequest(
  { secrets: [paymongoWebhookSecret, paymongoSecretKey], region: "asia-southeast1" },
  async (req, res) => {
    const signatureHeader = req.header("Paymongo-Signature");
    if (!signatureHeader) {
      res.status(401).send("Missing Paymongo-Signature header.");
      return;
    }

    const rawBody = (req as unknown as { rawBody: Buffer }).rawBody;
    if (!rawBody || !isValidSignature(rawBody, signatureHeader, paymongoWebhookSecret.value())) {
      logger.warn("paymongoWebhook: signature mismatch");
      res.status(401).send("Invalid signature.");
      return;
    }

    const payload = req.body as {
      data?: {
        attributes?: {
          type?: string;
          data?: { id?: string; attributes?: Record<string, unknown> };
        };
      };
    };

    const eventType = payload?.data?.attributes?.type;
    logger.info("paymongoWebhook event", { eventType, payload });

    if (eventType !== "payment.paid" && eventType !== "payment.failed") {
      // Acknowledge everything else so PayMongo does not retry-storm us.
      res.status(200).send("ignored");
      return;
    }

    const paymentResource = payload?.data?.attributes?.data;
    const attrs = paymentResource?.attributes ?? {};
    const paymentIntentId =
      (attrs["payment_intent_id"] as string | undefined) ??
      ((attrs["data"] as Record<string, unknown> | undefined)?.["attributes"] as
        | Record<string, unknown>
        | undefined)?.["payment_intent_id"] as string | undefined;

    if (!paymentIntentId) {
      logger.error("paymongoWebhook: could not locate payment_intent_id in payload", { payload });
      res.status(200).send("no payment_intent_id found — logged for inspection");
      return;
    }

    const paymentsSnap = await db
      .collection("payments")
      .where("paymongoIntentId", "==", paymentIntentId)
      .limit(1)
      .get();

    // A valid signature proves the sender holds the webhook secret; it is not proof of
    // payment. Nothing is settled or failed until PayMongo's own API — asked with our
    // secret key — agrees, so a leaked webhook secret cannot mark anything paid. When
    // PayMongo can't confirm yet, answer 503 so it retries the delivery later.
    const client = new PayMongoClient(paymongoSecretKey.value());

    if (paymentsSnap.empty) {
      // Not a listing fee — it may be a bid deposit. Checked before orders because
      // deposits are the model going forward; orders are only still here until the
      // in-flight ones drain.
      const depositSnap = await db
        .collection("bidIntents")
        .where("paymongoIntentId", "==", paymentIntentId)
        .limit(1)
        .get();
      if (!depositSnap.empty) {
        if (eventType !== "payment.paid") {
          // One failed scan uses up that QR. The intent stays open; a new code
          // is what the next scan pays. The poll does this too — the webhook
          // has often never arrived.
          const deposit = depositSnap.docs[0];
          await refreshQrOnDoc(
            client,
            deposit.ref,
            deposit.data(),
            paymentIntentId,
            deposit.data().bidderUid as string | undefined
          );
          res.status(200).send("attempt failed; deposit still open");
          return;
        }
        if ((await checkIntentWithPayMongo(client, paymentIntentId)) !== "paid") {
          logger.warn("paymongoWebhook: deposit not confirmed by PayMongo", { paymentIntentId });
          res.status(503).send("payment not confirmed by PayMongo yet");
          return;
        }
        // A fast path only. checkBidDeposit does the same thing when the app polls, and
        // that is what actually places bids here — this webhook has never arrived.
        await commitDeposit(depositSnap.docs[0].ref, "webhook");
        res.status(200).send("ok");
        return;
      }

      // Or a buyer paying for a won auction. Only a paid event can settle an order; a
      // failed one leaves it waiting for a new code.
      const orderSnap = await db
        .collection("orders")
        .where("paymongoIntentId", "==", paymentIntentId)
        .limit(1)
        .get();
      if (orderSnap.empty) {
        logger.error("paymongoWebhook: no matching payments, deposits or orders doc", { paymentIntentId });
        res.status(200).send("no matching payment record");
        return;
      }
      if (eventType !== "payment.paid") {
        const order = orderSnap.docs[0];
        await refreshQrOnDoc(
          client,
          order.ref,
          order.data(),
          paymentIntentId,
          order.data().buyerUid as string | undefined
        );
        res.status(200).send("attempt failed; order still open");
        return;
      }
      if ((await checkIntentWithPayMongo(client, paymentIntentId)) !== "paid") {
        logger.warn("paymongoWebhook: order payment not confirmed by PayMongo", { paymentIntentId });
        res.status(503).send("payment not confirmed by PayMongo yet");
        return;
      }
      await settleOrderPaid(paymentIntentId);
      res.status(200).send("ok");
      return;
    }

    const paymentDoc = paymentsSnap.docs[0];
    const payment = paymentDoc.data();

    if (payment.status === "paid") {
      res.status(200).send("already processed");
      return;
    }

    // Settles (shared with the reconcilers) only if PayMongo confirms the payment.
    const verdict = await reconcilePayment(client, paymentDoc.ref, paymentIntentId, "webhook");
    if (verdict === "paid") {
      res.status(200).send("ok");
      return;
    }
    if (verdict === "unknown" || eventType === "payment.paid") {
      logger.warn("paymongoWebhook: event not confirmed by PayMongo", { paymentIntentId, eventType, verdict });
      res.status(503).send("payment not confirmed by PayMongo yet");
      return;
    }

    // payment.failed is one attempt. PayMongo puts the intent back to
    // awaiting_payment_method so a new QR can be attached. Marking the doc
    // failed and reverting the listing made the next successful scan a no-op.
    logger.info("paymongoWebhook: payment attempt failed; QR stays open", { paymentIntentId });
    await refreshQrOnDoc(
      client,
      paymentDoc.ref,
      payment,
      paymentIntentId,
      payment.uid as string | undefined
    );
    res.status(200).send("attempt failed; payment still open");
  }
);

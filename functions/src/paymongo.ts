import * as logger from "firebase-functions/logger";
import { HttpsError } from "firebase-functions/v2/https";

const PAYMONGO_BASE_URL = "https://api.paymongo.com/v1";

/**
 * A rejection from PayMongo, carrying the HTTP status so callers can tell "our account is
 * misconfigured" (401) from "the network wobbled" — they need very different messages.
 */
export class PayMongoError extends Error {
  constructor(message: string, readonly status: number) {
    super(message);
    this.name = "PayMongoError";
  }
}

/**
 * Turns a PayMongo failure into something safe to show a student.
 *
 * A 401 means *our* secret key is wrong — which is not the student's problem and not
 * something retrying fixes, so it says so plainly rather than surfacing as a bare
 * "INTERNAL". PayMongo's own wording is never passed through: it talks about API keys.
 */
export function paymentUnavailable(err: unknown): HttpsError {
  if (err instanceof PayMongoError && err.status === 401) {
    logger.error("PayMongo rejected our secret key — PAYMONGO_SECRET_KEY is wrong or unset");
    return new HttpsError(
      "failed-precondition",
      "Payments aren't set up yet. Let a NUTrade admin know — this isn't something you did."
    );
  }
  return new HttpsError(
    "unavailable",
    "Couldn't reach the payment provider. Try again in a moment."
  );
}

/**
 * Thin wrapper over the PayMongo REST API. PayMongo uses HTTP Basic auth with
 * the secret key as the username and an empty password.
 *
 * NOTE on response shapes: PayMongo's Payment Intent / Payment Method / QRPh
 * "next_action" payload has a couple of documented variants (a hosted
 * `redirect.url` for e-wallets like GCash, and a `code.image_url` /
 * `code.image` for inline QR rendering). We extract every field we can find
 * defensively and log the raw response, so the first real test call can be
 * used to confirm/adjust the exact path — see `extractQrFromNextAction`.
 */
export class PayMongoClient {
  constructor(private readonly secretKey: string) {}

  private authHeader(): string {
    const token = Buffer.from(`${this.secretKey}:`).toString("base64");
    return `Basic ${token}`;
  }

  private async request<T>(method: string, path: string, body?: unknown): Promise<T> {
    const res = await fetch(`${PAYMONGO_BASE_URL}${path}`, {
      method,
      headers: {
        "Content-Type": "application/json",
        "Authorization": this.authHeader(),
      },
      body: body ? JSON.stringify(body) : undefined,
    });

    const json = (await res.json()) as unknown;
    if (!res.ok) {
      logger.error("PayMongo API error", { path, status: res.status, body: json });
      const message =
        (json as { errors?: Array<{ detail?: string }> })?.errors?.[0]?.detail ??
        `PayMongo request failed with status ${res.status}`;
      throw new PayMongoError(message, res.status);
    }
    return json as T;
  }

  /** Creates a Payment Intent for the given amount (centavos, PHP). */
  async createPaymentIntent(amountCentavos: number, description: string) {
    return this.request<PaymongoResource>("POST", "/payment_intents", {
      data: {
        attributes: {
          amount: amountCentavos,
          currency: "PHP",
          capture_type: "automatic",
          payment_method_allowed: ["qrph"],
          payment_method_options: { qrph: { auto_capture: true } },
          description,
        },
      },
    });
  }

  /** Creates a QRPh Payment Method. */
  async createQrPhPaymentMethod(billingName: string, billingEmail: string) {
    return this.request<PaymongoResource>("POST", "/payment_methods", {
      data: {
        attributes: {
          type: "qrph",
          billing: { name: billingName, email: billingEmail },
        },
      },
    });
  }

  /** Attaches a Payment Method to a Payment Intent, triggering QR generation. */
  async attachPaymentMethod(paymentIntentId: string, paymentMethodId: string, clientKey: string) {
    return this.request<PaymongoResource>(
      "POST",
      `/payment_intents/${paymentIntentId}/attach`,
      {
        data: {
          attributes: {
            payment_method: paymentMethodId,
            client_key: clientKey,
          },
        },
      }
    );
  }

  async retrievePaymentIntent(paymentIntentId: string) {
    return this.request<PaymongoResource>("GET", `/payment_intents/${paymentIntentId}`);
  }

  /**
   * Refunds a *payment*, not an intent — PayMongo refunds the charge, and an intent can
   * hold more than one. Use {@link paymentIdFromIntent} to get the id.
   *
   * QR Ph is refundable by API, with one hole: a QR Ph payment made through **Maya**
   * checkout cannot be refunded at all, and Maya partial refunds are barred until the
   * following day. Callers must have a path that does not depend on this succeeding.
   */
  async createRefund(paymentId: string, amountCentavos: number, reason: string) {
    return this.request<PaymongoResource>("POST", "/refunds", {
      data: {
        attributes: {
          amount: amountCentavos,
          payment_id: paymentId,
          reason,
        },
      },
    });
  }
}

/** The id of the settled charge behind an intent, or null when nothing was captured. */
export function paymentIdFromIntent(resource: PaymongoResource): string | null {
  const payments = resource.data.attributes["payments"] as Array<{ id?: string; attributes?: { status?: string } }> | undefined;
  if (!Array.isArray(payments)) return null;

  const paid = payments.find((p) => p?.attributes?.status === "paid") ?? payments[0];
  return paid?.id ?? null;
}

export interface PaymongoResource {
  data: {
    id: string;
    type: string;
    attributes: Record<string, unknown>;
  };
}

/**
 * Best-effort extraction of a renderable QR (image URL, base64 image, or raw
 * payload) plus an optional hosted checkout URL from a Payment Intent's
 * `next_action`. Logs the raw shape so it can be corrected from real traffic.
 */
export function extractQrFromNextAction(resource: PaymongoResource): {
  qrImageUrl?: string;
  qrImageBase64?: string;
  qrPayload?: string;
  redirectUrl?: string;
  /** ISO time PayMongo stops accepting this code (seen live: ~30 minutes after minting). */
  expiresAt?: string;
} {
  const nextAction = resource.data.attributes["next_action"] as Record<string, unknown> | undefined;
  logger.info("PayMongo next_action", { paymentIntentId: resource.data.id, nextAction });

  if (!nextAction) return {};

  const code = nextAction["code"] as Record<string, unknown> | undefined;
  const redirect = nextAction["redirect"] as Record<string, unknown> | undefined;

  return {
    qrImageUrl: (code?.["image_url"] as string) ?? undefined,
    qrImageBase64: (code?.["image"] as string) ?? undefined,
    qrPayload: (code?.["data"] as string) ?? (code?.["payload"] as string) ?? undefined,
    redirectUrl: (redirect?.["url"] as string) ?? undefined,
    expiresAt: (code?.["expires_at"] as string) ?? undefined,
  };
}

/**
 * PayMongo's own word that a Payment Intent was paid. Used to settle a fee without the
 * webhook — the answer comes from PayMongo with our secret key, never from a student.
 */
export function intentIsPaid(intent: PaymongoResource): boolean {
  const attrs = intent.data.attributes;
  if (attrs["status"] === "succeeded") return true;
  const payments = (attrs["payments"] as Array<{ attributes?: { status?: string } }> | undefined) ?? [];
  return payments.some((p) => p.attributes?.status === "paid");
}

/**
 * Mints a QR Ph code for an amount: payment intent, then a QRPh payment method, then
 * attach — which is what makes PayMongo generate the code.
 *
 * Shared by both money flows, because they are the same three calls: the seller paying
 * a listing fee (createQrPayment) and the buyer paying the winning bid (createOrderQrPayment).
 */
export async function mintQrPhPayment(
  client: PayMongoClient,
  params: {
    amountCentavos: number;
    description: string;
    billingName: string;
    billingEmail: string;
  }
) {
  const intent = await client.createPaymentIntent(params.amountCentavos, params.description);
  const method = await client.createQrPhPaymentMethod(params.billingName, params.billingEmail);

  const clientKey = intent.data.attributes["client_key"] as string;
  const attached = await client.attachPaymentMethod(intent.data.id, method.data.id, clientKey);

  return { intentId: intent.data.id, qr: extractQrFromNextAction(attached) };
}

# NUTrade Firebase Functions

Added in **Phase 4**. TypeScript, 2nd-gen Cloud Functions. Deployed to project `nutrade-a25c7`.

| File | Trigger | Responsibility |
| --- | --- | --- |
| `src/createQrPayment.ts` | Callable | Verify caller owns a `draft`/`pending_payment` listing, publish immediately for a verified seller's free first auction, otherwise create a PayMongo Payment Intent + `qrph` method, write `payments/{id}` with `qrExpiresAt`, return the QR + expiry. |
| `src/paymongoWebhook.ts` | HTTPS | Verify `Paymongo-Signature` (HMAC SHA-256, constant-time compare). On `payment.paid`, and only after PayMongo's API confirms it, post the listing into the admin queue. On `payment.failed`, leave the payment open and attach a new QR — one declined scan is not a cancelled fee. Always `200` once accepted so PayMongo doesn't retry-storm us. |
| `src/expireStalePayments.ts` | Scheduled (every 5 min) | PayMongo has no webhook event for an abandoned/expired QR — sweep `payments` with `status: awaiting_payment` past `qrExpiresAt` and revert the linked listing to `draft`. |
| `firestore.rules` | — | `listings` updates are Admin-SDK only (client can only `create`/`delete` a `draft`); `bids` subcollection is Function-only; `payments` / `transactions` / `counters` writes are Admin-SDK only. |

The PayMongo **secret key** (`PAYMONGO_SECRET_KEY`) and webhook signing secret (`PAYMONGO_WEBHOOK_SECRET`)
are Cloud Functions secrets (Secret Manager) — never in the MAUI app.

**FCM push is backend-only for now.** `notifySellerListingLive` in `paymongoWebhook.ts` reads
`users/{uid}.fcmTokens` (see `NUTrade1.Core/Models/UserProfile.cs`) and no-ops silently if it's
empty, which it always is today — the MAUI client has no Firebase Messaging plugin wired in yet
(Phase 2+ auth/Firestore integration hasn't started; the app is still 100% stub-service-backed).
Wiring that up — registering for a token per platform and writing it to the user doc — is a
separate follow-up task.

**Still open:** `placeBid` (atomic bid transactions) and `closeExpiredAuctions` (scheduled cron
to settle auctions whose `auctionEndsAt` has passed) per the architecture spec — not yet built.

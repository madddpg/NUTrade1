# Integration plan — updated specification (2026-09-24)

Against `NUTrade Platform Architecture & Specification (Updated)`. This records what the
document asks for, what already exists, and the four places the document contradicts a
decision already taken — with the call that was made on each.

## Decisions taken

| Question | Decision | Consequence |
| --- | --- | --- |
| Domain gating | **Any email + OTP stays.** | The document's `@nu-lipa.edu.ph` paragraph is superseded. Registration, `startSignup` and the Brevo mail path are unchanged. |
| Bid payments | **Deposit at bid time.** | The live *orders* model (winning bidder pays the full bid afterwards) is retired. This is the largest change in the plan. |
| Deposit amount | **15% of the bid**, not a flat ₱50. | `BID_DEPOSIT_PERCENT = 15` and `depositFor()` in `constants.ts` are already correct; no constant changes needed. The document's "₱50" is superseded. |
| Free listing | **First listing ever stays.** | The document's "free whenever active posts = 0" is superseded. `createQrPayment` is unchanged. |

One constraint that is not a decision: **the PayMongo webhook has never arrived in this
project.** Listing fees settle by asking PayMongo (`checkListingPayment`). Deposits will
reconcile the same way. `paymongoWebhook` stays deployed and correct, but nothing in the
new work may *depend* on it firing.

One open detail: the document says a bid deposit window of 30 minutes;
`DEPOSIT_INTENT_EXPIRY_MINUTES` in `constants.ts` says 15. The plan below uses **30**, as
the newer number.

## Already built

| Document feature | Where |
| --- | --- |
| Feed, hourly publish window, pull-to-refresh | `publishScheduledListings`, `RefreshView` on `FeedPage` |
| 1–4 photos, condition grading, meetup zone | `PostTradeViewModel`, `ImageCache` (downsamples to 1280px) |
| ₱0 first / ₱10 standard / ₱20 priority pin | `ADDITIONAL_FEE_CENTAVOS`, `PRIORITY_FEE_CENTAVOS`, `createQrPayment` |
| QR Ph gate and expiry sweeps | `createQrPayment`, `expireStalePayments`, `releaseUnpaidOrders` |
| Dual-confirmation handshake | `markTradeCompleted` — requires both participants |
| Signature-verified webhook, revenue counters | `paymongoWebhook`, `counters`, `transactions` |

Live and **absent from the document** — these stay: the listing-approval gate
(`pending_approval` + admin queue), email OTP and Brevo delivery, and forgot-password.

## Phase 1 — Wallet and ledger

Nothing below can settle anywhere until money has somewhere to land.

- `wallets/{uid}`: `balanceCentavos`, `updatedAt`. Server-written only; owner-readable.
- `ledgerEntries/{entryId}`: append-only. `uid`, `kind`
  (`deposit_credit` | `forfeit` | `refund_credit` | `payout`), `amountCentavos`,
  `relatedListingId`, `relatedBidId`, `createdAt`. Owner-readable, never client-written.
- `requestPayout` callable (student) and `markPayoutPaid` (admin), mirroring the existing
  admin-action pattern.
- `firestore.rules`: both collections `allow write: if false`.

Every balance change goes through a transaction that writes the wallet **and** its ledger
entry together, so the balance is always reconstructible from the entries.

## Phase 2 — Bid commitment deposits

Un-parks `functions/wip-deposits/`. Read its README first: `requestBid` is truncated, and
`commitDepositPaid`, the expiry sweep and the `BID_STATUS` uses were never written.

1. `requestBid` — creates the bid in `pending_deposit` and mints a PayMongo QR intent for
   `depositFor(bidAmount)`.
2. `checkBidDeposit` — the reconcile callable, modelled on `checkListingPayment`. This is
   what actually moves a bid to `active_locked`, because the webhook cannot be relied on.
3. `paymongoWebhook` — fall through to `commitDepositPaid` as well, as a fast path when it
   does fire. Not the only path.
4. `expireDepositIntents` — scheduled sweep releasing bids whose 30-minute window lapsed.
5. `matching.ts` — take the parked version: award creates **no order** and moves the
   listing to `pending_meetup`.
6. Client: `IBidService.PlaceBidAsync` gains the deposit-QR step; `OrderPaymentPage` is
   repurposed as the deposit screen.

**Migration risk.** `createOrderQrPayment` / `releaseUnpaidOrders` are live and may have
in-flight orders. Keep both deployed and exported until existing `orders` documents have
drained, then remove in a separate change. Do not delete `src/orders.ts` in this phase —
that is exactly what broke the build on 2026-09-19.

## Phase 3 — Deposit resolution

Superseding the earlier note that deposits are non-refundable: **a deposit is refundable**
(decided 2026-09-24). It ends in exactly one of four places, and every transition writes a
ledger entry so a dispute is settled by reading rows rather than by argument.

| Trigger | Deposit status | Where the money goes |
| --- | --- | --- |
| Both sides confirm the handover | `credited_to_seller` | Seller's wallet |
| Seller cancels or fails to fulfil | `refunded_to_buyer` | Back to the buyer |
| Buyer outbid, or the auction is lost | `refunded_to_buyer` | Back to the buyer |
| Winning bidder does not turn up | `forfeited` | Platform treasury, plus a strike |

Forfeit is the only path that keeps the money, which makes the deposit a commitment rather
than a fee.

### Two constraints the specification does not account for

**There is no hold to release.** `createPaymentIntent` uses `capture_type: "automatic"` with
`qrph: { auto_capture: true }`, so a deposit is captured the moment it is scanned — the money
genuinely leaves the student's GCash and lands in the NUTrade merchant account. "The
temporary hold is instantly lifted" is not achievable on QR Ph; a return is either a real
PayMongo refund (days to settle) or an in-app credit. The escrow is ours, in Firestore.

**QR Ph paid through Maya cannot be refunded at all.** PayMongo supports QR Ph refunds by
API and dashboard, but excludes payments made on Maya checkout, and restricts Maya partial
refunds to the following day. Any design that assumes a refund always succeeds will strand
those students, so the refund path needs a fallback that does not depend on PayMongo.

Both push the same way: in-app credit is the reliable settlement, and a PayMongo refund is
the nicety that is attempted where it works. The policy decided on 2026-09-24:

| Path | Settlement |
| --- | --- |
| Outbid / auction lost | **In-app credit only.** No PayMongo call at all — it fires on every outbid, and the student almost always wants to bid again. |
| Seller cancelled | **Attempt a PayMongo refund**, and fall back to in-app credit when it fails (Maya). The student is told which happened. |
| Handover confirmed | Credit to the seller's wallet. |
| No-show | Forfeited to the treasury, plus a strike on the buyer. |

A student who genuinely wants cash out of an in-app credit uses the payout queue built in
Phase 1 — which reaches Maya, because an admin sends it by hand.

### Work

- `resolveDeposit(depositId, outcome)` — the one place a deposit changes state, writing the
  deposit document, the ledger entry and the wallet move in a single transaction.
- `markTradeCompleted` calls it with `credited_to_seller`.
- `placeBid` calls it with `outbid` for the bid it displaces — this fires on *every* outbid,
  so it must be cheap.
- `closeExpiredAuctions` calls it with `auction_lost` for every non-winning bid.
- `reportNoShow` calls it with `forfeited` and increments `strikes` on the buyer.
- A PayMongo refund helper (`POST /refunds`) used where the chosen policy asks for it, with
  the outcome recorded on the deposit so a failed refund is visible rather than silent.
- Admin screen to resolve disputed no-shows, following `ListingApprovalsPage`.

## Phase 4 — Listing types

Today every listing is a fixed 24-hour auction. The document also wants standard listings
and open-to-offer swaps.

- `ListingKind` enum: `Auction` | `Standard` | `Swap`, defaulting to `Auction` so existing
  documents keep working.
- Post form picks the kind; auction-only fields (starting bid, increment) hide for the
  others.
- `closeExpiredAuctions` must skip non-auction kinds — it currently assumes every active
  listing has an `auctionEndsAt`.
- Feed card and detail page need a kind badge and different primary actions.

## Phase 5 — Offline cache

- SQLite-backed cache of the feed and the current profile.
- 60-minute background sync, alongside the existing in-memory 60-second reuse window.
- Feed reads cache-first, then refreshes — this is also the fix for the cold-start stall
  noted in `architecture-and-security.md`.

## Phase 6 — Developer revenue dashboard

An in-app admin screen rather than the Blazor portal the original spec described (that was
dropped). Reads `counters` and `transactions`: gross revenue split by stream (standard
fees, priority pins, forfeited deposits), and a payment audit trail of PayMongo intent ids,
uids and deposit statuses.

## Sequencing

Phases 1 → 2 → 3 are one connected body of work and must run in order. Phases 4, 5 and 6
are independent of each other and of the chain, and can be reordered freely.

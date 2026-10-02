# Web admin panel: one database with the app

The MAUI app and the web admin panel share one Firebase project (`nutrade-a25c7`) and
**one** Firestore database, `(default)`. There is no second database. The panel reads the
app's collections and changes them only through the app's Cloud Functions.

A Firestore database has exactly one set of security rules. Those rules live in this
repo (`firestore.rules`) and are deployed from this repo only. A deploy from the web
repo must not replace them.

## 1. Stop treating the panel as a second system

| Do this | Not this |
|---|---|
| `getFirestore(app)` — `(default)` | `getFirestore(app, "web")` |
| This repo's `firestore.rules` | `firestore.web.rules`, or any rules file in the web repo |
| This repo's functions (`codebase: "default"`) | A second `approveListing`, `rejectListing`, `createQrPayment`, or `paymongoWebhook` |
| `listings`, `users`, `transactions`, … | `marketplace`, `products`, `items`, `posts` |

Do not create a database named `web`. If one already exists, leave it. Live data is in
`(default)`, and nothing in the app reads the other one.

The web repo's `firebase.json` must not deploy Firestore rules. Drop the `firestore`
block. With `"database": "web"` it maintains a database the app ignores; without a
database name it targets `(default)` and replaces the app's rules, which is how the app
lost wallets, chats and orders the last time both sides deployed.

Functions share one namespace per project and region (`asia-southeast1`). A function
deployed under an app function's name replaces the app's. The panel's own
`paymongoWebhook` has already done that once, and it accepted unsigned
`payment.paid` events. Do not deploy it again. If the web repo still ships functions,
give them `"codebase": "web"` and names that do not collide, and delete the duplicates:
`approveListing`, `rejectListing`, `createQrPayment`, `paymongoWebhook`.

## 2. Open the shared database

```js
import { getFirestore } from "firebase/firestore";
import { getFunctions, httpsCallable } from "firebase/functions";

const db = getFirestore(app); // (default) — the only database
const functions = getFunctions(app, "asia-southeast1");
```

The panel is a client. `firestore.rules` lets a signed-in admin **read** the collections
below and lets **nobody** write them from the client, admin included. Listing status,
bids, payments, wallets and payouts all move inside Cloud Functions.

## 3. Read the app's collections

Amounts on listings, bids, wallets and payouts are **centavos**. Listing-fee rows in
`transactions` also carry `amount` in pesos, which is the figure to show as revenue.
Times are Firestore timestamps. An ISO string in `auctionEndsAt` is skipped by the app
and by `closeExpiredAuctions`, so the panel must not write listing times itself.

### `listings/{id}` — the moderation queue

Query `status == "pending_approval"`. The seller is `ownerUid` (the same uid is also
stored as `sellerUid`). The fee that was actually paid is `paidPackage` (`"Free"`,
`"Additional"`, or `"Priority"`), not the draft's `package`. Approval honours
`paidPackage`.

Other fields the queue needs: `title`, `description`, `photos` (1–4 download URLs),
`condition` (`New`, `LikeNew`, `Good`, `Worn`), `category` (`Uniforms`, `Textbooks`,
`AcademicSupplies`, `Other`) plus `categoryOther`, `campusZone` plus `campusZoneOther`,
`startingBidCentavos`, `minIncrementCentavos`, `submittedForApprovalAt`.

Status values, in order: `draft`, `pending_payment`, `pending_approval`, `active`,
`matched`, `pending_meetup`, `completed`, `expired`, `cancelled`, `rejected`.

### `users/{uid}`

`displayName`, `firstName`, `lastName`, `email`, `program` (`SACE`, `SAHS`, `SABM`,
`SHS`), `photoUrl`, `tradesCompleted`, `verificationStatus`, `role`. A new account is
already `verificationStatus: "verified"` because signup proves the email with a
one-time code. There is no Student ID review queue. `studentId` is blank on new
accounts.

### Revenue

`transactions` is the ledger. Each paid listing fee is one document: `amount` (pesos),
`amountCentavos`, `packageType`, `status` (`"paid"`), `timestamp`, `userId`,
`userEmail`, `listingId`, `paymentId`. `counters/revenue` holds `grossCentavos` and
`transactionCount`.

`payments/{id}` is the QR attempt behind a fee (`uid`, `listingId`, `amount`,
`package`, `status`). A wallet "payment failed" is one scan: `status` stays
`awaiting_payment` and a new code is attached. A doc stuck at `failed` is from
an older webhook and can still settle if PayMongo later reports the intent paid.
Admins can read it; students can read only their own.

### Bid credit and disputes

There is no cash-out. Drop the payout screen. `payoutRequests` is historical. Any
document still `status == "requested"` should be finished with `declinePayout` so the
held amount returns as bid credit. Do not send new ones, and do not call `markPayoutPaid`.

`wallets/{uid}.balanceCentavos` is bid credit for the next deposit, not money the
student can withdraw. `ledgerEntries` explains it: `uid`, `kind`, `amountCentavos`
(negative when credit is spent), `note`, `createdAt`.

| `kind` | Meaning |
| --- | --- |
| `refund_credit` | Bond returned to the bidder (outbid, lost, showed up, seller cancelled, dispute refunded) |
| `bid_credit_spent` | Credit reserved for a deposit. Always negative |
| `forfeit_credit` | No-show bond given to the seller |
| `deposit_credit` | Old handover rows that credited the seller. No new ones |
| `payout` | Old cash-out rows. No new ones |

`disputes` with `status == "open"` is a no-show report. Fields: `listingTitle`,
`sellerUid`, `buyerUid`, `depositCentavos`, `note`, `chatId`.

`counters/revenue.forfeitCreditCentavos` is the sum of no-show bonds awarded to
sellers. It is not fee revenue.

## 4. Change data only through these callables

Every one of these requires the `role: "admin"` custom claim (see below). Pass the
argument names exactly.

```js
await httpsCallable(functions, "approveListing")({ listingId });
await httpsCallable(functions, "rejectListing")({ listingId, reason });

// Only to clear a payout that was already requested before cash-out was removed.
await httpsCallable(functions, "declinePayout")({ payoutRequestId, reason });

await httpsCallable(functions, "resolveDispute")({ disputeId, resolution: "forfeit" });
// resolution is "forfeit" or "refund"

await httpsCallable(functions, "setUserVerification")({ uid, verified: false, reason });
```

`approveListing` starts the 24-hour auction at approval, not at payment. Priority
(`paidPackage == "Priority"`) is pinned and visible immediately. Free and Additional
stay `isVisible: false` until the next hourly refresh (`visibleFrom`). The seller is
notified by `onListingUpdated`; the panel does not write a notification document.

`rejectListing` is terminal. `reason` is shown to the seller, trimmed to 300
characters. A paid fee is not refunded here.

`declinePayout` returns an old cash-out request as bid credit. `requestPayout` now
refuses, and `markPayoutPaid` should not be used for anything new.

`resolveDispute` with `"forfeit"` gives the buyer's deposit to the seller as bid
credit and adds a strike on the buyer. `"refund"` returns it to the bidder as bid
credit and adds no strike. Neither path pays the platform.

`setUserVerification` revokes or restores the `verified` custom claim. It is not how a
student becomes verified in the first place — `completeSignup` does that.

Do not update `listings` from the client, including the Admin SDK path inside a
duplicate function. Direct writes skip the auction clock, the pin, and the feed slot.

## 5. Admin accounts

Rules and callables trust a custom claim, not an email list, an `admins` collection,
or `role` on the user's profile document. `bootstrapAdmin` (this repo) sets
`role: "admin"`:

```
curl -X POST https://asia-southeast1-nutrade-a25c7.cloudfunctions.net/bootstrapAdmin \
  -H "Content-Type: application/json" -H "X-Bootstrap-Secret: <secret>" \
  -d '{"email":"<admin email>"}'
```

The admin signs out and back in so the token carries the claim.

`approveListing` and `rejectListing` also accept the panel's older `admin: true`
claim. `declinePayout`, `resolveDispute` and `setUserVerification` do not — they
require `role == "admin"`. Grant that claim. Do not keep a second admin flag.

## 6. Collections the panel used to own

`admin_security_codes`, `notifications`, `marketplace`, `products`, `items` and
`posts` are not part of the app. The shared rules do not allow them, and the app
never reads them. Drop those screens or point them at the collections in section 3.

- Second-factor codes → Firebase Auth plus the `role: "admin"` claim.
- Notices to a student → the app's Cloud Functions (`onListingUpdated` and the fee
  settlement path). There is no `notifications` collection.
- Catalogue / marketplace → `listings`.

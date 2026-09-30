# NUTrade

Campus swap app for National University – Lipa. A .NET MAUI client on a Firebase
backend, with listing fees collected over PayMongo QR Ph.

Build plan (full architecture, data model, payment flow, phases):
<https://claude.ai/code/artifact/6a4c6767-c443-43fb-bc6b-11f64aa85b2c>

## Solution layout

| Project | Framework | Purpose |
| --- | --- | --- |
| `NUTrade1` | `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, `net10.0-windows` | MAUI app head — Views, ViewModels, Services, Shell |
| `NUTrade1.Core` | `net10.0` | Models, enums, service abstractions, DTOs — no MAUI reference |
| `NUTrade1.Tests` | `net10.0` | xUnit tests for Core and rule logic |
| `functions/` | Node / TypeScript (2nd gen) | Firebase Cloud Functions — added in Phase 4 |

## Status — live on Firebase

The app is backed by the real `nutrade-a25c7` Firebase project. There is no seeded
or hard-coded content in the client: the feed, bid ladders, seller profiles and
countdowns all come from Firestore.

### How the client talks to Firebase

`NUTrade1/Services/Firebase/` implements every `NUTrade1.Core` abstraction over the
**Firebase REST APIs** rather than a native SDK binding, so one implementation runs on
all four heads — Windows included, which keeps the desktop debug loop usable.

| Concern | Transport |
| --- | --- |
| Sign-in, token refresh, custom claims | Identity Toolkit REST (`FirebaseAuthService`) |
| Reads and draft writes | Firestore REST (`FirestoreClient`, `Fs`, `Q`) |
| Bids, matching, payments, verification | Callable Cloud Functions (`FunctionsClient`) |
| Listing photos, ID photos | Firebase Storage REST (`FirebaseStorageService`) |

The one thing REST gives up is the snapshot listener. `ObserveListing` and
`ObserveMessages` poll instead (`PollingObserver`) — 5s for listings, 2s for chat.

### What the client is not allowed to do

`firestore.rules` denies client updates to `listings` outright, and the `bids`
subcollection is server-write-only. A client can create a listing **only** as a
`draft`; publishing, pinning, bid counters, matching and trade completion are all
Admin-SDK writes from `functions/`. The `verified` custom claim gates posting and
bidding.

### Registration

Deliberately light. A student signs up with **any** email and a password, or with their
Google account — there is no campus-domain restriction. They then enter a six-digit code
mailed to that address, and `verifyEmailOtp` grants the `verified` claim. That is the
whole gate; there is no Student ID review step. See the 2026-09-18 amendment in
`docs/architecture-and-security.md` for what that trades away and how to reverse it.

The Shell routes on this: signed out → `login`, signed in but unverified →
`verifyemail`, verified with no profile → `profilesetup`, otherwise the feed.

**Forgot password** works the same way, from the link on the sign-in card: email →
six-digit code → new password, all on one screen (`forgotpassword`). Firebase Auth's own
reset email is deliberately not used — it would come from Google rather than Brevo and
would send the student out to a web page. Completing a reset signs every other device out.

### Cloud Functions (all deployed to `asia-southeast1`)

| Function | Trigger | Purpose |
| --- | --- | --- |
| `placeBid` | callable | Validates and records a bid in one transaction; demotes the previous top bid |
| `approveBid` / `declineBid` / `withdrawBid` | callable | Seller and bidder actions; re-derives the top bid |
| `closeExpiredAuctions` | every 5 min | Settles auctions past `auctionEndsAt` — awards or expires |
| `markTradeCompleted` | callable | Closes a trade once **both** sides confirm |
| `onChatMessageCreated` | Firestore | Mirrors the latest message onto the parent chat |
| `createQrPayment` | callable | Free first auction publishes instantly; otherwise mints a QR Ph code |
| `paymongoWebhook` | HTTPS | HMAC-verified; publishes the listing when the fee clears |
| `expireStalePayments` | every 5 min | Reverts listings stranded behind a dead QR |
| `sendEmailOtp` | callable | Mails a 6-digit code to the caller's own address (hashed + salted, 10 min, 60s resend cooldown) |
| `verifyEmailOtp` | callable | Checks the code and grants the `verified` claim |
| `startSignup` / `verifySignupCode` / `completeSignup` | callable | Registration, code first; nothing is created until the code is proven |
| `startPasswordReset` / `verifyPasswordResetCode` / `completePasswordReset` | callable | Forgot password, same shape; the last one also revokes every refresh token |
| `setUserVerification` | callable | Admin-only; revokes (or restores) the `verified` claim |
| `bootstrapAdmin` | HTTPS | Secret-gated; mints the first admin account |
| `seedDevData` | callable | Admin-only Firestore seeder (see below) |

Every endpoint that mails a code also charges it against a per-address allowance —
`OTP_MAX_SENDS_PER_WINDOW` codes per `OTP_SEND_WINDOW_MINUTES`, counted in
`mailRateLimits/{sha256(email)}`. The 60-second cooldown caps the *rate*; this caps the
*total*, which is what stops a script parked on one address at a code a minute. Add a
Firestore **TTL policy on `mailRateLimits.expiresAt`** so spent counters are swept —
without one the collection grows a document per address forever.

## Web admin panel

The MAUI app and the web admin panel are one system. They share the `nutrade-a25c7`
project and **one** Firestore database, `(default)` — not a separate `web` database.
The panel reads the app's collections (`listings`, `users`, `transactions`,
`payoutRequests`, `disputes`, …) and changes them only by calling this repo's Cloud
Functions (`approveListing`, `rejectListing`, `markPayoutPaid`, `resolveDispute`, …).

Rules are deployed from this repo only. A `firebase deploy` from the web repo must not
ship its own `firestore.rules`, and it must not redeploy `paymongoWebhook`,
`approveListing`, `rejectListing` or `createQrPayment` under those names.

Wiring, field names and the callables the panel should use:
[`docs/web-panel-database.md`](docs/web-panel-database.md).

## Seeding real data

`scripts/seed.mjs` writes five student accounts and four live auctions with their
bid ladders — straight into Firestore, in exactly the shape the real flow produces.
It uses only public endpoints, so no service-account key is needed locally.

```sh
node scripts/seed.mjs \
  --admin-email you@nu-lipa.edu.ph \
  --admin-password "..." \
  --bootstrap-secret "$(firebase functions:secrets:access ADMIN_BOOTSTRAP_SECRET)" \
  --seed-password "..."
```

Re-running is safe: it clears each listing's bids, and any chat, chat messages and trade
record a previous settlement left behind, so the state is the same every time.

`--ends-in-minutes -2` seeds auctions that have **already ended**. That is the practical
way to watch `closeExpiredAuctions` settle them — it sweeps every 5 minutes, awards each
auction to its top live bid, and opens the chat room.

Seeded students are pre-verified. A real signup stays unverified — and is held on the
verify-email screen — until the student enters the code mailed to them.

## One-time project setup

In the Firebase console:

1. **Authentication → Get started → Email/Password → Enable**
2. **Storage → Get started** (creates the bucket that `storage.rules` governs)
3. *(for "Continue with Google")* **Authentication → Sign-in method → Google → Enable**

Then the mailer. Every NUTrade email — verification codes and password-reset codes —
goes out through [Brevo](https://brevo.com). Sign up, create an API key under **SMTP &
API → API keys**, and:

```sh
firebase functions:secrets:set BREVO_API_KEY      # paste the key when prompted
```

Until that key is set, `sendEmailOtp`, `startSignup` and `startPasswordReset` all fail with
a clear message and nobody can register or reset a password.

Brevo will only send from an address it has validated, so `BREVO_SENDER_EMAIL` must be set
in `functions/.env` too (see `functions/.env.example`) — there is no default, and every
send fails with a message naming the variable until there is one. Two ways to get a valid
sender:

- **For development:** the address you signed up to Brevo with is already validated, and
  unlike Resend's shared sender it delivers to *anyone*. Put that address in
  `BREVO_SENDER_EMAIL` and real students can register today.
- **For launch:** authenticate a domain at **Senders, Domains & Dedicated IPs → Domains**,
  add the three records Brevo gives you (a `brevo-code` TXT, a DKIM TXT at
  `mail._domainkey`, and `include:spf.brevo.com` in the domain's SPF), then send as
  anything on it. Check propagation with:

```sh
node scripts/check-email-dns.mjs yourdomain.com
```

On Cloudflare every one of those records must be **DNS only (grey cloud)** — Cloudflare
proxies new records by default and a proxied DKIM or SPF record fails verification with a
misleading "not found". Note that a domain may only have one SPF record, so if one already
exists, edit it to add the include rather than adding a second.

Pointing `BREVO_SENDER_EMAIL` at an address Brevo has not validated breaks sending
entirely, including to your own address. Changing it needs a redeploy of the mailing
functions.

For Google sign-in you also need native OAuth clients — enabling the provider only creates
a Web client, which a native app cannot use. Create Desktop / Android / iOS clients under
**APIs & Services → Credentials** in the Google Cloud console and paste the ids into
`GoogleOAuth` in `NUTrade1/Services/Firebase/FirebaseSettings.cs`. Until they are filled in,
`IsGoogleSignInAvailable` is false and the button is hidden — email and password still work.

Then deploy the rules and functions:

```sh
FUNCTIONS_DISCOVERY_TIMEOUT=120 \
  firebase deploy --only firestore:rules,firestore:indexes,storage,functions
```

The env var matters. Without it the CLI gives itself 10 seconds to load the compiled
functions and decide what to deploy, and this codebase routinely misses that, failing with
`User code failed to load. Cannot determine backend specification. Timeout after 10000`.
It is not an error in your code — just retry with the timeout raised.

## Build

```sh
dotnet test NUTrade1.Tests/NUTrade1.Tests.csproj
dotnet build NUTrade1/NUTrade1.csproj -f net10.0-windows10.0.19041.0
dotnet build NUTrade1/NUTrade1.csproj -f net10.0-android
```

To click through the UI with no network, flip `UseStubServices` in `MauiProgram.cs`.
The `Stub*` services hold no seeded content — they start empty.

## Secrets

Nothing secret lives in the app. `FirebaseSettings.ApiKey` is a public project
identifier, protected by `firestore.rules` and the user's ID token. The values that
are secret live in Secret Manager and are read only by Cloud Functions:

| Secret | Used by |
| --- | --- |
| `PAYMONGO_SECRET_KEY` | `createQrPayment` |
| `PAYMONGO_WEBHOOK_SECRET` | `paymongoWebhook` |
| `ADMIN_BOOTSTRAP_SECRET` | `bootstrapAdmin` |
| `BREVO_API_KEY` | `sendEmailOtp`, `startSignup`, `startPasswordReset` |

The Google OAuth client ids and the desktop client "secret" are **not** in this table on
purpose: in an installed-app flow that secret is not confidential, which is why the broker
uses PKCE. They live in source alongside the Firebase API key.

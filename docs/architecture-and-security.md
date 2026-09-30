# NUTrade — Platform Architecture & Security Specification

> Canonical architecture/security reference for NUTrade. Supersedes the security and
> pricing notes in earlier planning material where they conflict.
>
> **Open decision:** this spec describes an **auction / bidding** marketplace. The
> current `NUTrade1.Core` model, ViewModels, and shipped UI are a **swap / trade**
> marketplace (`Listing` + `Offer`, "open to offers", "offer a swap"). The swap → auction
> refactor has **not** been done and is pending explicit go-ahead.

---

## 1. Zero-Trust Security Architecture

Security is built into every layer to prevent spoofing, double-bidding, client-side
state manipulation, unauthorized API access, and webhook forgery.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           CLIENT / MAUI FRONTEND                            │
│  - Storage: Encrypted SecureStorage (tokens)                                │
│  - Network: SSL pinning + domain check                                      │
│  - Gate: read-only feed for unverified accounts                             │
└──────────────────────────────────────┬──────────────────────────────────────┘
                      [HTTPS / TLS 1.3 transport layer]
┌──────────────────────────────────────▼──────────────────────────────────────┐
│                    FIREBASE AUTHENTICATION & IDENTITY                       │
│  - Email/Password or personal Google Auth                                   │
│  - Custom claims: { verified: boolean, role: "student" | "admin" }          │
└──────────────────────────────────────┬──────────────────────────────────────┘
┌──────────────────────────────────────▼──────────────────────────────────────┐
│                  FIRESTORE DB & STORAGE SECURITY RULES                      │
│  - Strict role-based access control (RBAC)                                  │
│  - Status & listing writes denied to client (server-only mutations)         │
│  - Storage ID photos: private folder, admin-only read                       │
└──────────────────────────────────────┬──────────────────────────────────────┘
┌──────────────────────────────────────▼──────────────────────────────────────┐
│                 BACKEND / 2ND-GEN CLOUD FUNCTIONS (Node.js)                 │
│  - Secret Manager: PayMongo API secret keys stored in KMS                   │
│  - HMAC SHA-256 webhook signature verification                              │
│  - Atomic Firestore transactions (concurrency / race-condition guard)       │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Core Security Controls Matrix

| Vulnerability / threat | Architectural mitigation | Technical enforcement |
|---|---|---|
| Client-side price / status tampering | No direct client writes to listing status, revenue, or active bids | Firestore rules deny client `update` to `status`, `currentHighestBid`, `isPinned`, `publishedAt` |
| PayMongo webhook forgery | Validate payload origin before executing financial events | `paymongoWebhook` computes HMAC SHA-256 with the PayMongo webhook signing secret and compares against the `Paymongo-Signature` header |
| API secret key exposure | Keep PayMongo secret keys off client devices | Secret keys in Google Cloud Secret Manager, accessed only by Cloud Functions via env vars |
| Bid race conditions / over-bidding | Atomic locking on concurrent bid submissions | Bids processed by the `placeBid` Cloud Function using `db.runTransaction()` to isolate and order incoming bids |
| Fake account / unauthorized access | Identity verification check | Unverified accounts blocked from `placeBid` / `createQrPayment` via custom auth claims + Firestore rules |
| Data leakage (student ID photos) | Protect physical ID photos from onboarding | Stored at `verification_ids/{uid}.jpg`; read access restricted to the `admin` role |

---

## 2. System Components & Functional Modules

### A. Client Mobile App (.NET MAUI)

**1. Brand-themed bidding marketplace & feed**
- NU navy + gold branding, category filter chips, search, highlighted **Featured Priority Auctions**.
- **1-hour feed auto-refresh** (battery + Firestore-read savings):
  - Feed listings cached locally in SQLite / memory with a timestamp.
  - Every 60 min the cache expires and issues a single-shot paginated fetch (`getDocs`).
  - Manual pull-to-refresh overrides the timer instantly.
  - On-screen auction countdown timers (`MM:SS` / `HH:MM`) render locally via `IDispatcherTimer` from `auctionEndsAt`.

**2. Student ID onboarding & verification engine**
- Auth: personal email registration (Google Sign-In or Email/Password).
- ID upload: unverified students submit a camera capture of their physical **NU Lipa Student ID card** during onboarding.
- Gated permissions: unverified users get a **pending** account status — can view feed items but **cannot** create listings, place bids, or send messages.

**3. Auction creation & entitlement engine**
- Seller inputs: **Starting Bid (₱)**, **Bid Increment Step (₱)**, **Auction Duration** (24h / 48h / 72h), optional **Reserve Price**.
- Photo & location: 1–4 photos via Firebase Storage, item condition, campus meetup zone (**Main Library, Student Hub, Campus Courtyard, Cafeteria**).
- Entitlement logic:
  - **Free first auction (₱0.00):** 1 active free auction slot per verified student. Auto-publishes immediately.
  - **Standard paid auction (₱10.00):** required for any additional active auction beyond the free quota.
  - **Featured Priority Pin (₱20.00):** optional upgrade — pins the post to the top of the feed with a gold badge.

**4. Live bidding engine & real-time sync**
- Calls the `placeBid` Cloud Function for server-validated bidding.
- Active item detail pages detach from the 1-hour cache and attach real-time Firestore listeners to `listings/{id}/bids` to stream bid updates.

**5. Dynamic QR Ph payment gate**
- Displays dynamic PayMongo QR Ph codes (GCash / Maya compliant) with a **10-minute** expiry countdown for paid listing-fee settlement.
- Listens to Firestore status updates to auto-navigate on successful payment.

### B. Admin Web Portal (ASP.NET Core Blazor)

**1. Student ID verification review queue**
- Dashboard of pending student-ID photo uploads alongside user account details.
- Admins review physical ID photos, verify campus affiliation, toggle account state (approved / rejected). Approval triggers custom auth claim `verified: true`.

**2. Moderation & audit engine**
- Real-time monitoring of active auctions, draft purges, flagged listings.
- Manual overrides to unpublish non-compliant listings or approve manual verification overrides.

### C. Cloud Infrastructure & Backend Services (Firebase & Cloud Functions)

**1. Security-hardened Firestore rules (`firestore.rules`)**

```javascript
rules_version = '2';
service cloud.firestore {
  match /databases/{database}/documents {

    // Helper functions
    function isSignedIn() { return request.auth != null; }
    function isOwner(userId) { return isSignedIn() && request.auth.uid == userId; }
    function isVerified() { return isSignedIn() && request.auth.token.verified == true; }
    function isAdmin() { return isSignedIn() && request.auth.token.role == 'admin'; }

    // Users
    match /users/{userId} {
      allow read: if isSignedIn();
      allow create: if isOwner(userId) && request.resource.data.verificationStatus == 'pending';
      allow update: if isAdmin(); // only admins verify accounts
    }

    // Listings
    match /listings/{listingId} {
      allow read: if isSignedIn();
      allow create: if isVerified()
        && request.resource.data.ownerUid == request.auth.uid
        && request.resource.data.status in ['draft', 'pending_payment', 'active'];
      allow update: if false;  // deny direct client updates (handled via Functions)
      allow delete: if isOwner(resource.data.ownerUid) && resource.data.status == 'draft';

      // Bids subcollection
      match /bids/{bidId} {
        allow read: if isSignedIn();
        allow write: if false; // must call placeBid Cloud Function
      }
    }

    // Transactions & revenue counters (server-only)
    match /transactions/{txId} { allow read: if isOwner(resource.data.userId) || isAdmin(); allow write: if false; }
    match /counters/{counterId} { allow read: if isAdmin(); allow write: if false; }
  }
}
```

**2. Atomic bid validation brokerage — `placeBid`** (2nd-gen, TypeScript)
- Validates `request.auth.token.verified === true`.
- Loads `listings/{listingId}` inside a Firestore transaction.
- Asserts `status === 'active'`, `auctionEndsAt > now`, `buyerUid !== ownerUid`.
- Asserts `bidAmount >= currentHighestBid + minIncrement`.
- Appends the bid to `listings/{id}/bids` and updates `currentHighestBid` + `highestBidderUid` atomically.

**3. Secure payment brokerage — `createQrPayment`**
- Checks the user's active published auctions (`status == 'active'`).
- If active count is `0` **and** package is Basic → immediately sets `status: 'active'` without calling PayMongo.
- If active count `>= 1` **or** package is Priority → calls PayMongo `POST /v1/payment_intents` using secrets from KMS.
- Generates a dynamic QR Ph code with `expiry_seconds: 600` (10 min) and writes metadata to `payments/{paymentId}`.

**4. Cryptographically signature-verified webhook — `paymongoWebhook`**
- Extracts the `Paymongo-Signature` header.
- Computes HMAC SHA-256 over the **raw payload body** with the webhook signing secret; on mismatch returns `401`.
- Firestore transaction: idempotency check (duplicate processing), update listing `status: 'active'`, set `auctionEndsAt = now + duration`, create a `transaction` record, increment platform revenue atomically.

**5. Scheduled auction closure — `closeExpiredAuctions`** (cron, every 5 min)
- Queries `listings` where `status == 'active'` and `auctionEndsAt <= now`.
- **Bids present:** `status → matched`, create a private `chats/{chatId}` between seller and winning bidder, send FCM ("You won the auction!" / "Your item was sold!").
- **No bids:** `status → expired`.

---

## 3. End-to-End System Data & Security Flow

```
Student registers (personal email) ──► uploads physical NU Student ID card photo
                                                     │
                                                     ▼
                                       photo stored in private Storage
                                                     │
                                                     ▼
                                     admin verifies ID via Admin Web Portal
                                                     │
                                                     ▼
                                     custom auth claim issued: verified: true
                                                     │
                                                     ▼
                            seller submits auction draft (starting price, duration)
                                                     │
                                                     ▼
                             Cloud Function checks seller's active auction count
                                                     │
          ┌──────────────────────────────────────────┴──────────────────────────────────────────┐
 [ active count = 0 AND package = Basic ]                       [ active count ≥ 1 OR package = Priority Pin ]
          │                                                                                     │
          ▼                                                                                     ▼
 status set to 'active'                                                          status set to 'pending_payment'
 (bypasses payment gate)                                                                        │
          │                                                                                     ▼
          │                                                              app calls createQrPayment
          │                                                                                     │
          │                                                              PayMongo generates QR Ph (10-min expiry)
          │                                                                                     │
          │                                                              HMAC SHA-256 signature-verified webhook
          └──────────────────────────────────────────┬──────────────────────────────────────────┘
                                                     ▼
                                         auction live on campus feed
      ┌──────────────────────────────────────────────┴──────────────────────────────────────────┐
      │  • feed viewers: 1-hour local cache (or manual pull-to-refresh)                          │
      │  • active bidders: real-time listener; bids sent via atomic placeBid function            │
      └──────────────────────────────────────────────┬──────────────────────────────────────────┘
                                                     ▼
                          scheduled closeExpiredAuctions runs every 5 min:
                      • validates expiry timestamp
                      • status → 'matched'
                      • creates private chat room for seller & winner
                      • triggers FCM push notifications
```

---

## 4. Operational Summary Matrix

| Package tier | Seller active-limit condition | Price | Security & execution pathway |
|---|---|---|---|
| Basic auction (1st post) | active auctions = 0 & verified claim = true | ₱0.00 | Server-side validation; direct publish; `status: active` |
| Basic auction (2nd+ post) | active auctions ≥ 1 & verified claim = true | ₱10.00 | PayMongo QR Ph → HMAC webhook verification → `status: active` |
| Featured auction (any post) | selected at creation & verified claim = true | ₱20.00 | PayMongo QR Ph → HMAC webhook verification → `status: active`, `isPinned: true` |

---

## Reconciliation notes vs. the current codebase (2026-09-03)

| Area | Current code / plan | This spec |
|---|---|---|
| Core model | swap: `Listing` + `Offer` (`OpenToOffers` / `RequestedItem`) | **auction**: `Listing` + `startingBid`, `bidIncrement`, `auctionDuration`, `reservePrice`, `auctionEndsAt`, `currentHighestBid`, `highestBidderUid`; `bids/` subcollection |
| Verification | `@nu-lipa.edu.ph` Google domain restriction | personal email **or** Google + physical Student ID photo → admin review → `verified` custom claim |
| Auth methods | Google (NU domain) only | Google (personal) or Email/Password |
| Pricing | ₱10 Basic / ₱20 Priority, no free tier | **₱0 first auction**, ₱10 additional, ₱20 Featured Pin |
| `ListingStatus` | `Draft, PendingPayment, Published, Matched, Completed, Expired, Cancelled` | `draft, pending_payment, active, matched, expired` (`active` == old `Published`) |
| `CampusZone` | MainLibrary, StudentHub, VersNestBooth, Gymnasium, MainLobby | Main Library, Student Hub, **Campus Courtyard**, **Cafeteria** |
| Admin portal | none | new **ASP.NET Core Blazor** project |
| Feed refresh | `RefreshView` + paginated `GetPublishedFeedAsync` | 1-hour local SQLite/memory cache w/ timestamp, single-shot `getDocs`, manual override, `IDispatcherTimer` countdowns |
| Backend functions | `createQrPayment` (planned) | + `placeBid`, `paymongoWebhook` (HMAC), `closeExpiredAuctions` (5-min cron) |
| Client hardening | planned | SSL pinning + domain check, encrypted SecureStorage for tokens |
| Storage | listing photos only | + `verification_ids/{uid}.jpg`, admin-only read |

---

## Amendment — registration simplified to email OTP (2026-09-18)

Agreed with the product owner and now implemented. This **supersedes** the Student ID
photo flow described above wherever the two disagree.

| Area | This spec said | What the code now does |
|---|---|---|
| Who may register | Personal email or Google, then upload a Student ID photo | Personal email or Google. Any address — the `@nu-lipa.edu.ph` restriction is gone from the codebase entirely |
| How `verified` is granted | Admin reviews the ID photo in the Blazor portal | `verifyEmailOtp` grants it when the student enters the 6-digit code mailed to their address |
| What `verified` still gates | Creating listings, placing bids | Unchanged — `firestore.rules`, `placeBid` and `createQrPayment` all still require it |
| Admin portal | Required to onboard anyone | No longer on the critical path. `setUserVerification` remains, for **revoking** an account |

### What this trades away, deliberately

Email OTP proves the person controls an inbox. It does **not** prove they are an NU – Lipa
student, which is what the ID photo was for. The campus marketplace is therefore open to
anyone who can receive email. The mitigations that remain:

- `setUserVerification` can revoke an account at any time, and `sendEmailOtp` /
  `verifyEmailOtp` both refuse a `verificationStatus: 'rejected'` account — so a revoked
  student cannot simply re-run the OTP flow to let themselves back in.
- Every privileged write is still server-brokered and attributed to a UID, so abuse is
  traceable and reversible after the fact.

If student-only access becomes a requirement again, the cheapest route back is to re-apply
a domain check at `sendEmailOtp` (the address is read from the ID token there, so it cannot
be spoofed by the client) rather than to rebuild the photo review queue.

### OTP implementation notes

- Codes are 6 digits from `crypto.randomInt`, stored **hashed** (SHA-256 over a
  per-challenge random salt) in `otpChallenges/{uid}`, which is `allow read, write: if false`
  — a client that could read its own challenge could brute-force the hash offline, and one
  that could write it could reset its own attempt counter.
- 10-minute expiry, 5 wrong attempts before the challenge is burned, 60-second resend
  cooldown.
- The destination address is taken from the caller's ID token, never from the request body,
  so the endpoint cannot be used to mail codes to arbitrary people.
- Delivery is Brevo's transactional API, keyed by the `BREVO_API_KEY` secret, sending from
  the validated address in `BREVO_SENDER_EMAIL`. See the 2026-09-24 amendment below.

## Amendment — Brevo, and forgot password (2026-09-24)

### Email delivery

All NUTrade email now goes through one provider, Brevo's transactional API
(`POST https://api.brevo.com/v3/smtp/email`), in `functions/src/brevo.ts`. It replaced two
earlier attempts, for the same reason each time — neither could reach a real student:

| | Why it was dropped |
| --- | --- |
| Resend | The shared `onboarding@resend.dev` sender only delivers to the address that owns the Resend account, and its test mode blocked mail to anyone else. A verified domain would have fixed it; we did not have one. |
| Gmail SMTP (nodemailer + App Password) | Sends only as that Gmail account, caps out near 500 messages a day, and offers no delivery log to check when a student says no code arrived. |

Brevo sends from a validated sender — either a verified address or any address on an
authenticated domain — delivers to anyone, and shows every message in its dashboard. The
API key is a Secret Manager secret; the sender address is plain config in
`functions/.env` (`BREVO_SENDER_EMAIL`), because it is not secret and is the thing most
likely to change. There is deliberately **no default sender**: a guess would fail every
send with Brevo's `invalid_parameter`, so the mailer refuses up front with a message
naming the variable.

There is no SDK — it is one POST, and Node 20 has global `fetch`. `nodemailer` is gone
from `functions/package.json`.

### Forgot password

`functions/src/passwordReset.ts` adds `startPasswordReset` → `verifyPasswordResetCode` →
`completePasswordReset`, the same three-step shape as registration, backed by
`passwordResetChallenges/{sha256(email)}` and the same hashed-code machinery (10-minute
code, 5 attempts, 60-second cooldown, then a 15-minute one-time token stored only as a
hash).

Firebase Auth's `sendPasswordResetEmail` was rejected on two counts: it mails from
Google's servers with Google's template, so it could not go through Brevo at all, and it
drops the student into a web page — the one flow in the app that would leave the app.

Two properties worth keeping in mind when editing it:

- **`completePasswordReset` revokes every refresh token.** Someone resetting because their
  password leaked is not helped by a new password that leaves the thief's session working.
- **Both mail caps apply.** The 60-second resend cooldown lives on the challenge document;
  the per-address total (`consumeMailAllowance`, `mailRateLimits/{sha256(email)}`) is kept
  separately *because* challenge documents are deleted when a code is spent, expires or is
  burned by wrong guesses — keeping the counter there would hand the limit straight back.
  The allowance is charged before the account lookup, so a registered and an unregistered
  address behave identically even once the cap trips.
- **`startPasswordReset` answers the same way whether or not the address is registered**,
  and refuses `verificationStatus: 'rejected'` accounts the same silent way, so an admin's
  revocation cannot be undone by a reset. This is not a guarantee about the system as a
  whole — `startSignup` still answers "an account with this email already exists" — it just
  declines to be a second address oracle on the endpoint a stranger would reach for.

## Amendment — post form, OTP modal and feed responsiveness (2026-09-24)

### Meetup locations are a fixed list

`CampusZone` was a guess at NU–Lipa's layout (Main Library, Student Hub, Ver's Nest Booth,
Main Lobby). It is now the real list: Student Lounge, Gymnasium, Accounting and Registrar
Office, ITSO, SDAO, AVR, and **Others**, which requires the seller to type the place into
`campusZoneOther`. `Category` gained the same treatment — it is now required (the form no
longer pre-picks Uniforms), and `Other` requires `categoryOther`.

Enums travel as their member name and `Fs.Enum` falls back on an unrecognised one, so
**listings created before this change read back with `CampusZone.Unknown`** and simply show
no meetup location. Nothing breaks; the old rows are just unlabelled. The meetup location is
also now shown on the listing detail page — it was collected and then never displayed to the
buyer, which made choosing it pointless.

### The code step is a modal

`OtpModal` is an in-page overlay, not a pushed modal page: registration, forgot-password and
email verification all keep one ViewModel, and the code, the cooldown and the error belong to
the wizard that opened it. A second page would have to hand results back for no gain.

### What actually made the app feel slow

Four things, all measured against the REST transport's per-call latency rather than guessed:

| | Was | Now |
| --- | --- | --- |
| Feed search | A Firestore `runQuery` per keystroke, with `if (IsBusy) return` silently dropping most of them — and the filtering was client-side anyway, so every one of those round trips returned the same 20 documents | `ApplySearch` filters the page already in memory; no network at all |
| Profile load | One bid query **per listing, awaited inside the loop** — eight listings meant eight sequential round trips on a blank screen — after three more chained reads | Independent reads go out together; the bid queries fan out through `Task.WhenAll` |
| Tab switches | `OnAppearing` refetched the whole feed every time, so Home → Profile → Home was two full queries with an empty list in between | A loaded feed is reused for 60s; pull-to-refresh still forces a read |
| Profile claims | `RefreshClaimsAsync` forced a `securetoken` round trip on every appearance, each one raising `AuthStateChanged` and making the Shell re-evaluate its gate | Skipped when the claims were read in the last minute; `force: true` for callers that just changed them |

Filling a bound `ObservableCollection` with `Clear()` then an `Add` per item raises N+1
`CollectionChanged` events and `CollectionView` re-measures on each. `ObservableRangeCollection.ReplaceAll`
raises one Reset instead; the feed and both Profile lists use it.

Not addressed, in rough order of remaining value:

- **Feed images.** `ImageCache` already downsamples uploads to 1280px/80% JPEG, but the feed
  decodes that full bitmap into a 124px-tall box for every card. A thumbnail variant written
  at upload time — or platform-level downsampling on decode — is the next real win.
- **Offset pagination.** `GetActiveFeedAsync` pages with `offset`, and Firestore reads (and
  bills for) every skipped document, so page five reads a hundred to return twenty. Cursor
  pagination on `publishedAt` would fix both cost and latency.
- **Cold start.** `RestoreSessionAsync` refreshes the token before the Shell can route, so the
  first paint waits on one network call. Unavoidable without caching the last known claims.

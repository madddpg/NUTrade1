# Web admin panel: its own Firestore database

The app and the web admin panel share one Firebase project (`nutrade-a25c7`). A Firestore
database has exactly **one** set of security rules, so while both lived in the same database,
each side's `firebase deploy` replaced the other's rules: the app lost access to wallets,
chats and orders, and the panel lost its collections.

From now on:

| | App | Web admin panel |
|---|---|---|
| Database | `(default)` | `web` (new) |
| Rules file | `firestore.rules` in the app repo | `firestore.web.rules` in the web repo |
| Deployed from | the app repo only | the web repo only |
| Holds | users, listings, bids, chats, orders, payments, transactions, wallets, payouts… | admin_security_codes, notifications, marketplace, products, items, posts |

The panel still **reads** app data (listings, users, payments, transactions, payouts) from
`(default)` as an admin, and changes listings only through the app's Cloud Functions.

## 1. Create the `web` database (once)

```
firebase firestore:databases:create web --location nam5 --project nutrade-a25c7
```

`nam5` matches `(default)`. Any location works; it can't be changed later.

## 2. Web repo `firebase.json`: name the database

The `firestore` entry **must** name `web`. Without `"database"`, it targets `(default)` and
wipes the app's rules again.

```json
{
  "firestore": { "database": "web", "rules": "firestore.web.rules" }
}
```

Keep it a single object, not an array. In the array form, `firebase deploy --only
firestore:rules` treats `rules` as a database name, matches nothing, and silently deploys
no rules at all.

The app repo's `firebase.json` names `(default)` the same way, so neither repo can touch the
other's rules.

## 3. `firestore.web.rules` (starting point)

Rules in the `web` database can only see documents in `web`, so admin checks use custom
claims, not a lookup in `(default)`.

```
rules_version = '2';
service cloud.firestore {
  match /databases/{database}/documents {
    function isSignedIn() { return request.auth != null; }
    function isAdmin() {
      return isSignedIn() && (
        request.auth.token.get('role', '') == 'admin'
        || request.auth.token.get('admin', false) == true
      );
    }

    // Second-factor codes for admin sign-in.
    match /admin_security_codes/{codeId} {
      allow read, write: if isAdmin();
    }

    // Notices to a student: the recipient reads theirs, only admins write.
    match /notifications/{notificationId} {
      allow read: if isSignedIn() && (resource.data.userId == request.auth.uid || isAdmin());
      allow write: if isAdmin();
    }

    match /marketplace/{id} { allow read: if isSignedIn(); allow write: if isAdmin(); }
    match /products/{id}    { allow read: if isSignedIn(); allow write: if isAdmin(); }
    match /items/{id}       { allow read: if isSignedIn(); allow write: if isAdmin(); }
    match /posts/{id}       { allow read: if isSignedIn(); allow write: if isAdmin(); }
  }
}
```

## 4. Panel code

```js
import { getFirestore } from "firebase/firestore";
import { getFunctions, httpsCallable } from "firebase/functions";

const appDb = getFirestore(app);         // (default): listings, users, payments… read-only
const webDb = getFirestore(app, "web");  // the panel's own collections

const functions = getFunctions(app, "asia-southeast1");
await httpsCallable(functions, "approveListing")({ listingId });
await httpsCallable(functions, "rejectListing")({ listingId, reason });
```

- **Approve or reject only through those two callables.** The app's rules refuse direct
  writes to listings. The callables set the auction clock, the feed slot (Priority shows at
  once and is pinned; Free and Additional join at the next hourly refresh) and notify the
  seller.
- Revenue: the app writes `transactions` in `(default)` in the panel's shape (`amount` in
  pesos, `packageType`, `status`, `timestamp`, `userEmail`).

## 5. Admin accounts

The app's rules and callables recognise admins by custom claim only. Emails, an `admins`
collection and `role` on the user's profile document are not trusted. Grant a web admin the
claim once with the app's `bootstrapAdmin` endpoint (needs `ADMIN_BOOTSTRAP_SECRET`):

```
curl -X POST https://asia-southeast1-nutrade-a25c7.cloudfunctions.net/bootstrapAdmin \
  -H "Content-Type: application/json" -H "X-Bootstrap-Secret: <secret>" \
  -d '{"email":"<admin email>"}'
```

The admin then signs out and back in so their token carries the claim.

## 6. Cloud Functions: don't reuse the app's names

Functions share one namespace per project and region, so a function deployed with an
app function's name replaces the app's. The panel's `paymongoWebhook` has already replaced
the app's, and the panel's `index.js` also exports `approveListing`, `rejectListing` and
`createQrPayment`.

- Give the panel's functions their own codebase (`"codebase": "web"` in its `firebase.json`),
  so a deploy never offers to delete the app's.
- Rename or remove anything that clashes. `sendAdminSecurityCode` is fine; the other four
  duplicate app functions and should go.
- The panel's `paymongoWebhook` skips signature verification when the
  `paymongo-signature` header is missing, so anyone can post a fake "payment.paid". The app's
  `onListingUpdated` trigger now checks such listings with PayMongo and reverts unpaid ones,
  but the webhook should reject unsigned requests.

## 7. Existing data

`admin_security_codes` and `notifications` documents already in `(default)` stay there, and
once the app's rules are deployed clients can no longer read them. Copy any worth keeping
into `web` with the Admin SDK or the console.

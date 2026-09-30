# Listing UX plan — 2026-09-24

Four requests: view a listing's photos, save or share the payment QR, stop sellers
bidding on their own items, and add a review screen before paying to post.

## Standards applied throughout

- **MVVM.** Screen logic lives in view models, and navigation goes through `INavigationService`.
  Only view behaviour stays in code-behind: gestures, and carousel position quirks.
- **Rules live in Core, with tests.** `BidEligibility` and `QrImageData` are plain C# in
  `NUTrade1.Core`, covered by `BidEligibilityTests` and `QrImageDataTests`.
- **Defence in depth.** The UI hides what isn't allowed, the view model refuses it, and the
  server enforces it.
- **Nothing is written before the user commits.** The draft is created on *Post Listing*, not
  on *Continue to review*.
- **Least privilege.** iOS asks for add-only Photos access. Android asks for storage
  permission only on version 9 and older.
- **Accessibility.** Photos, arrows and buttons carry `SemanticProperties`. Desktop gets arrows
  because a mouse can't swipe.
- **No double submits.** Every command that writes or navigates is guarded by busy state.

## 1. Photo viewing

- `Controls/PhotoCarousel`: every photo, whole and never cropped. It has a "2 / 4" counter,
  page dots and desktop arrows. Tapping a photo opens the viewer.
- `Views/PhotoViewerPage`: full screen on a dark background. You can swipe, pinch or
  double-tap to zoom (up to 4x), and drag while zoomed. Swiping is locked while zoomed. It opens
  at the photo you tapped.
- `Controls/ZoomableImage`: its pan gesture is attached only while zoomed, so it never takes
  the carousel's swipe.
- Used on Listing detail and on Review Listing.
- WinUI quirk: an animated programmatic position change snapped back to 0, so both carousels
  set `IsScrollAnimated="False"`.

## 2. Saving and sharing the payment QR

- PayMongo's `code.image_url` is a `data:image/png;base64,…` URI. A string image source can't
  draw that, so `QrImageData` decodes it to bytes.
- `Controls/PaymentQrView` draws the QR and adds **Save QR** and **Share**, plus a hint:
  *GCash/Maya → Pay QR → Upload QR*. It is used on both the listing-fee screen and the
  bid-deposit screen.
- `Services/QrImageSaver` (`IQrImageSaver`) saves to a different place on each platform:
  - Android 10 and later: MediaStore → `Pictures/NUTrade`, with no permission needed.
  - Android 9 and older: needs `WRITE_EXTERNAL_STORAGE`, which is declared with
    `maxSdkVersion=28`.
  - iOS / Mac: PhotoKit with add-only access (`NSPhotoLibraryAddUsageDescription`).
  - Windows: `Pictures\NUTrade`.
  - Share uses the system share sheet everywhere.

## 3. Sellers can't bid on their own items

| Layer | Guard |
|---|---|
| UI | The bid form is hidden until the listing loads and never shown to the seller. The seller sees "Sellers can't bid on their own items." |
| View model | `PlaceBid` runs `BidEligibility.WhyNot` before any deposit is requested. |
| `requestBid` | Already refused the seller before any QR is minted (unchanged). |
| `commitDeposit` | **New:** a seller's deposit is treated as stale and returned. It never becomes a bid. |
| Firestore rules | `bids` are `write: false`, so they are only ever written by Functions (unchanged). |

## 4. Review Listing before payment

Flow: Post form → **Continue to review** (checks the form, writes nothing) → **Review
Listing** (photos, category • condition, title, bids, meetup, package, payment method) →
**Post Listing** (creates the draft, uploads the photos) → payment screen. Review is replaced
by payment (`../payment`), so Back never returns to a second Post button. The form clears
itself through `ListingSubmittedMessage`.

## Verification

- Core: 95/95 tests pass.
- Builds: Windows and Android.
- Checked in the Windows head:
  - The Review screen renders.
  - The viewer opens at the tapped photo.
  - The QR renders from a data URI.
  - **Save QR** writes a byte-exact PNG to `Pictures\NUTrade`.
- **Not verified:**
  - Swipe, pinch and double-tap zoom, and the arrow taps. Synthetic mouse input doesn't reach
    this window.
  - Android or iOS on a device.
  - iOS doesn't build on this machine.
  - A real PayMongo QR.
- **Deployed 2026-09-25:** the `commitDeposit` guard, via (quote the list in PowerShell)
  `firebase deploy --only "functions:checkBidDeposit,functions:paymongoWebhook,functions:expireDepositIntents"`
  (these are the functions that call `commitDeposit`).

/**
 * Cross-cutting constants shared with the client. Keep these in sync with
 * NUTrade1.Core/NUTradeConstants.cs and NUTrade1.Core/Enums/ListingPackage.cs —
 * there is intentionally no shared package between the .NET client and this
 * Node backend, so a change to pricing/enum members must be mirrored by hand.
 */

import { randomInt } from "crypto";

export type ListingPackage = "Free" | "Additional" | "Priority";

/** Posting fee for every listing after a student's free first one, in centavos (₱10.00). */
export const ADDITIONAL_FEE_CENTAVOS = 1_000;

/** Featured Priority Pin upgrade fee, in centavos (₱20.00). */
export const PRIORITY_FEE_CENTAVOS = 2_000;

/** PayMongo QR Ph codes expire this many seconds after creation. */
export const QR_EXPIRY_SECONDS = 600;

export function feeForPackage(pkg: ListingPackage): number {
  switch (pkg) {
    case "Free":
      return 0;
    case "Additional":
      return ADDITIONAL_FEE_CENTAVOS;
    case "Priority":
      return PRIORITY_FEE_CENTAVOS;
    default:
      throw new Error(`Unknown listing package: ${pkg}`);
  }
}

/** Every function in this codebase deploys to the same Manila-adjacent region. */
export const REGION = "asia-southeast1";

/**
 * How a listing is offered. Member names match ListingKind in the app.
 * A document with no `kind` (everything written before this field) is an auction.
 */
export const LISTING_KIND = {
  auction: "Auction",
  standard: "Standard",
  swap: "Swap",
} as const;

export type ListingKindName = (typeof LISTING_KIND)[keyof typeof LISTING_KIND];

export function listingKind(listing: { kind?: unknown }): ListingKindName {
  if (listing.kind === LISTING_KIND.standard || listing.kind === LISTING_KIND.swap) {
    return listing.kind;
  }
  return LISTING_KIND.auction;
}

/** Firestore string values for ListingStatus / BidStatus, mirrored from the .NET enums. */
export const LISTING_STATUS = {
  draft: "draft",
  pendingPayment: "pending_payment",
  /** Paid (or free) and waiting on an admin before it may go live. */
  pendingApproval: "pending_approval",
  active: "active",
  matched: "matched",
  /** Won, but the two of them still have to meet and hand the item over. */
  pendingMeetup: "pending_meetup",
  completed: "completed",
  expired: "expired",
  cancelled: "cancelled",
  /** An admin turned it down. Terminal; `rejectionReason` says why. */
  rejected: "rejected",
} as const;

/**
 * The Free package is a student's first listing — once, ever. A listing has used it as
 * soon as it was submitted and not turned down: waiting for approval, live, or finished
 * in any way. A rejected listing gives the free post back, so a student whose first
 * attempt was refused can fix it and try again for free. Drafts and unpaid listings
 * never count.
 *
 * Mirrored by NUTradeConstants.UsesFreePost in NUTrade1.Core — the app hides the Free
 * option with the same rule, and createQrPayment enforces it.
 */
export const FREE_POST_USED_STATUSES: string[] = [
  LISTING_STATUS.pendingApproval,
  LISTING_STATUS.active,
  LISTING_STATUS.matched,
  LISTING_STATUS.completed,
  LISTING_STATUS.expired,
];

export const BID_STATUS = {
  pending: "pending",
  approved: "approved",
  declined: "declined",
  outbid: "outbid",
  withdrawn: "withdrawn",
} as const;

// ---- Orders -----------------------------------------------------------------
// What a won auction turns into: the winning bidder owes the seller the bid amount.
// Mirrored by NUTrade1.Core/Enums/OrderStatus.cs (FirestoreOrderService maps the spellings).

export const ORDER_STATUS = {
  /** Seller approved the bid; the buyer's payment window is running. */
  awaitingPayment: "awaiting_payment",
  /** Confirmed by PayMongo's signed webhook — never by either student. */
  paid: "paid",
  /** The window closed unpaid; the bid was struck out and the auction reopened. */
  released: "released",
  cancelled: "cancelled",
} as const;

/** How long the winning bidder has to pay before the bid is released. */
export const ORDER_PAYMENT_WINDOW_HOURS = 24;

/** No 0/O or 1/I, so a reference read aloud at a meetup can't be misheard. */
const ORDER_REFERENCE_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

/** A short, human-friendly order reference such as `NUT-7K2QXM`. */
export function orderReference(): string {
  let suffix = "";
  for (let i = 0; i < 6; i++) {
    suffix += ORDER_REFERENCE_ALPHABET[randomInt(ORDER_REFERENCE_ALPHABET.length)];
  }
  return `NUT-${suffix}`;
}

// ---- Email one-time codes ---------------------------------------------------
// Registration is deliberately light: any email, a 6-digit code to prove the
// student owns it, and that is the whole gate. There is no Student ID review in
// the signup path — see docs/architecture-and-security.md and the note in
// verifyEmailOtp about what this trades away.

/** How long a code stays usable. */
export const OTP_TTL_MINUTES = 10;

/** Wrong guesses before the challenge is burned and a new code is required. */
export const OTP_MAX_ATTEMPTS = 5;

/** Minimum gap between sends, so the endpoint can't be used to mail-bomb someone. */
export const OTP_RESEND_COOLDOWN_SECONDS = 60;

/**
 * Total codes one address may be mailed per window, across registration, verification and
 * password reset. The cooldown above caps the rate; this caps the total, which is what
 * stops a script parked on one address at a code a minute.
 *
 * Five is generous for the honest case — a student who mistypes their address, fixes it
 * and still needs a resend or two — and cheap for everyone else.
 */
export const OTP_MAX_SENDS_PER_WINDOW = 5;
export const OTP_SEND_WINDOW_MINUTES = 60;

/** How long a password-reset code stays usable. */
export const PASSWORD_RESET_TTL_MINUTES = 10;

/** How long a proven reset code stays spendable for actually setting the new password. */
export const PASSWORD_RESET_TOKEN_TTL_MINUTES = 15;

/**
 * Sender on every NUTrade email, in the shape Brevo's API wants.
 *
 * Brevo will only send from an address it has validated — a verified sender, or any
 * address on an authenticated domain — so there is no sensible default to fall back to:
 * a guess would fail every send with `invalid_parameter`. Returns null when unset, and
 * the mailer turns that into a message naming the variable to set.
 */
export function brevoSender(): { name: string; email: string } | null {
  const email = process.env.BREVO_SENDER_EMAIL?.trim();
  if (!email) return null;
  return { name: process.env.BREVO_SENDER_NAME?.trim() || "NUTrade", email };
}

// ---- Auctions ---------------------------------------------------------------
/**
 * Every auction runs the same length. The create form no longer asks — the
 * prototype dropped the 24/48/72 picker — so this is the single source of truth.
 */
export const AUCTION_DURATION_HOURS = 24;

/**
 * Photos per listing, per the "Add up to 4 photos" dropzone. Only the app enforces it;
 * mirrored by NUTradeConstants.MaxListingPhotos.
 */
export const MAX_LISTING_PHOTOS = 4;

// ---- Feed visibility --------------------------------------------------------
/**
 * "New regular listings join after the hourly refresh. Priority listings appear
 * immediately." Free and Additional listings are published with `isVisible: false`
 * and a `visibleFrom` set to the next top of the hour; publishScheduledListings
 * flips them. Priority skips the queue, which is most of what the fee buys.
 *
 * The feed filters on `isVisible == true` rather than on a `visibleFrom <= now`
 * inequality on purpose: Firestore would force that inequality to be the first
 * orderBy, which would break the pinned-first ordering.
 */
export function nextHourlyRefresh(fromMillis: number): number {
  const next = new Date(fromMillis);
  next.setUTCMinutes(0, 0, 0);
  next.setUTCHours(next.getUTCHours() + 1);
  return next.getTime();
}

/** A no-show report waiting on an admin. Only an admin can forfeit a deposit. */
export const DISPUTE_STATUS = {
  open: "open",
  resolved: "resolved",
} as const;

// ---- Wallet payouts ---------------------------------------------------------
/**
 * Below this, a payout costs more in admin time than it moves. Students accumulate
 * rather than draining a balance twenty pesos at a time.
 */
export const MIN_PAYOUT_CENTAVOS = 10_000;

export const PAYOUT_STATUS = {
  /** Student asked; the balance is already held back so it cannot be spent twice. */
  requested: "requested",
  /** An admin sent the money off-platform and said so. */
  paid: "paid",
  /** An admin refused it; the held balance goes back. */
  declined: "declined",
} as const;

/** The programs a student picks from at registration. Mirrored by NUTradeConstants.Programs. */
export const PROGRAMS = ["SACE", "SAHS", "SABM", "SHS"] as const;

/** Where an admin is to send the money. The app only ever stores what the student typed. */
export const PAYOUT_METHODS = ["gcash", "maya"] as const;
export type PayoutMethod = (typeof PAYOUT_METHODS)[number];

// ---- Bid commitment deposit -------------------------------------------------
/**
 * Anti-ghosting: a bid is not live until the bidder has put money behind it.
 *
 * THE ONE KNOB. The deposit is this percentage of the bid, and it is **REFUNDABLE** —
 * superseding the earlier non-refundable design (decided 2026-09-24). A deposit only
 * ever ends in one of three places, and the student must be told which before they pay:
 *
 *   - handover confirmed  -> credited to the seller's wallet
 *   - seller cancels      -> returned to the buyer
 *   - outbid or lost      -> returned to the buyer
 *
 * So the deposit is a commitment, not a fee: the only way to lose it is to be the
 * winning bidder and then fail to turn up.
 *
 * Set to the bottom of the agreed 15–20% band. Change this single value to move within
 * it, or set DEPOSIT_IS_PERCENTAGE to false to charge FLAT_DEPOSIT_CENTAVOS instead.
 */
export const BID_DEPOSIT_PERCENT = 15;

/** Used instead of the percentage when DEPOSIT_IS_PERCENTAGE is false. */
export const FLAT_DEPOSIT_CENTAVOS = 2_000;
export const DEPOSIT_IS_PERCENTAGE = true;

/**
 * Deposit owed for a bid of this size, in centavos, rounded up to the peso so students
 * are never asked to scan a QR for an amount with stray centavos.
 */
export function depositFor(bidAmountCentavos: number): number {
  const raw = DEPOSIT_IS_PERCENTAGE
    ? (bidAmountCentavos * BID_DEPOSIT_PERCENT) / 100
    : FLAT_DEPOSIT_CENTAVOS;
  return Math.ceil(raw / 100) * 100;
}

/**
 * The life of one deposit. Every transition is written to the ledger, so a dispute can
 * be settled by reading the entries rather than by argument.
 *
 * Note what "escrow" does and does not mean here. QR Ph captures immediately
 * (`capture_type: "automatic"`), so the money genuinely leaves the student's wallet and
 * sits in the NUTrade merchant account — there is no authorisation hold to release. The
 * escrow is ours, in Firestore, and a return is a real refund or an in-app credit.
 */
export const DEPOSIT_STATUS = {
  /** QR issued, waiting on PayMongo to say the money landed. */
  awaitingPayment: "awaiting_payment",
  /** Paid and held against this bid. The bid it was raised for is live. */
  lockedInEscrow: "locked_in_escrow",
  /** Older handovers credited the seller. New handovers return the bond to the bidder. */
  creditedToSeller: "credited_to_seller",
  /** Bond returned to the bidder as bid credit — outbid, lost, showed up, or seller cancelled. */
  refundedToBuyer: "refunded_to_buyer",
  /** The winning bidder never turned up. The bond becomes the seller's bid credit. */
  forfeited: "forfeited",
  /** The QR lapsed before payment, so the bid it was holding never went live. */
  expired: "expired",
} as const;

export type DepositStatus = (typeof DEPOSIT_STATUS)[keyof typeof DEPOSIT_STATUS];

/** Why a deposit went back, which decides whether it is refunded or merely credited. */
export const REFUND_REASON = {
  sellerCancelled: "seller_cancelled",
  outbid: "outbid",
  auctionLost: "auction_lost",
  /** Both sides confirmed the meetup. The buyer paid the full price in person. */
  tradeCompleted: "trade_completed",
} as const;

/**
 * How much of a deposit bid credit covers, and how much is still scanned.
 * Mirrors BidCredit.Split in NUTrade1.Core/BidCredit.cs.
 */
export function bidCreditSplit(
  balanceCentavos: number,
  depositCentavos: number
): { applied: number; qrDue: number } {
  if (depositCentavos <= 0) return { applied: 0, qrDue: 0 };
  const applied = balanceCentavos <= 0 ? 0 : Math.min(balanceCentavos, depositCentavos);
  return { applied, qrDue: depositCentavos - applied };
}

/**
 * A bid intent whose deposit never arrives is swept after this long.
 * The on-screen QR countdown uses PayMongo's own expiry when it sends one, and this
 * window otherwise, so the clock the student sees is the time they still have to pay.
 */
export const DEPOSIT_INTENT_EXPIRY_MINUTES = 30;

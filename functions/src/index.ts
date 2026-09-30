import "./admin";

// Auctions. There is deliberately no placeBid: a bid is only ever written by
// commitDeposit, once its commitment deposit has been confirmed paid. A callable that
// wrote a bid directly would make the deposit optional, which is the whole mechanism.
export { approveBid, declineBid, withdrawBid } from "./bidActions";
export { closeExpiredAuctions } from "./closeExpiredAuctions";

// Moderation — every new listing waits for an admin before it goes live.
export { approveListing, rejectListing } from "./listingApproval";
export { onListingUpdated } from "./listingReview";

// Feed visibility — regular listings join on the hour, Priority jumps the queue.
export { publishScheduledListings } from "./listingVisibility";

// Buyer pays the winning bid, by QR Ph through PayMongo.
export { createOrderQrPayment, releaseUnpaidOrders } from "./orders";

// Trading & chat
export { onChatMessageCreated, markTradeCompleted } from "./chat";

// Listing fees
export { createQrPayment, checkListingPayment } from "./createQrPayment";
export { paymongoWebhook } from "./paymongoWebhook";
export { expireStalePayments } from "./expireStalePayments";

// Email verification (the registration gate)
export { sendEmailOtp, verifyEmailOtp } from "./emailOtp";

// Registration, code first: email → code → name and password → account.
export { startSignup, verifySignupCode, completeSignup } from "./signup";

// Forgot password, the same shape: email → code → new password.
export { startPasswordReset, verifyPasswordResetCode, completePasswordReset } from "./passwordReset";

// Anti-ghosting bid deposits. checkBidDeposit is what actually places a bid — the
// PayMongo webhook has never arrived in this project.
export { requestBid, checkBidDeposit, expireDepositIntents } from "./deposits";

// When a matched trade ends badly: the seller pulling out, or a no-show dispute.
export { cancelTrade, reportNoShow, resolveDispute } from "./tradeResolution";

// Internal wallet: balances, the ledger behind them, and cashing out.
export { requestPayout, markPayoutPaid, declinePayout } from "./payouts";

// Admin moderation
export { bootstrapAdmin, setUserVerification } from "./adminActions";

// Development only — admin-gated Firestore seeder, invoked by scripts/seed.mjs.
export { seedDevData } from "./dev/seedDevData";

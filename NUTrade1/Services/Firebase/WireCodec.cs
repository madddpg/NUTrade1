using NUTrade1.Core;

namespace NUTrade1.Services.Firebase;

/// <summary>
/// Maps the two enums whose Firestore spellings are not just their .NET member names.
///
/// The backend is TypeScript and stores these as snake_case strings
/// (<c>pending_payment</c>, <c>pending_approval</c>, <c>awaiting_payment</c>) — they are part of the wire
/// contract in <c>functions/src/constants.ts</c> and in <c>firestore.rules</c>, which
/// matches on the literal values, so the client bends to them rather than the reverse.
///
/// The wallet enums (ledger kind, payout status, payout method) are snake_case or lower
/// case on the wire for the same reason and are mapped here too.
///
/// Every other enum on the wire (category, condition, campus zone, package, bid status,
/// chat status, message type) already round-trips through its member name and is read
/// with <see cref="Fs.Enum{T}"/> directly.
/// </summary>
public static class WireCodec
{
    public static string ToWire(ListingStatus status) => status switch
    {
        ListingStatus.Draft => "draft",
        ListingStatus.PendingPayment => "pending_payment",
        ListingStatus.PendingApproval => "pending_approval",
        ListingStatus.Active => "active",
        ListingStatus.Matched => "matched",
        ListingStatus.Completed => "completed",
        ListingStatus.Expired => "expired",
        ListingStatus.Cancelled => "cancelled",
        ListingStatus.Rejected => "rejected",
        _ => "draft",
    };

    public static ListingStatus ToListingStatus(string? wire) => wire switch
    {
        "draft" => ListingStatus.Draft,
        "pending_payment" => ListingStatus.PendingPayment,
        "pending_approval" => ListingStatus.PendingApproval,
        "active" => ListingStatus.Active,
        "matched" => ListingStatus.Matched,
        "completed" => ListingStatus.Completed,
        "expired" => ListingStatus.Expired,
        "cancelled" => ListingStatus.Cancelled,
        "rejected" => ListingStatus.Rejected,
        _ => ListingStatus.Draft,
    };

    public static DepositStatus ToDepositStatus(string? wire) => wire switch
    {
        "awaiting_payment" => DepositStatus.AwaitingPayment,
        "locked_in_escrow" => DepositStatus.LockedInEscrow,
        "credited_to_seller" => DepositStatus.CreditedToSeller,
        "refunded_to_buyer" => DepositStatus.RefundedToBuyer,
        "forfeited" => DepositStatus.Forfeited,
        "expired" => DepositStatus.Expired,
        _ => DepositStatus.Unknown,
    };

    public static LedgerKind ToLedgerKind(string? wire) => wire switch
    {
        "deposit_credit" => LedgerKind.DepositCredit,
        "forfeit" => LedgerKind.Forfeit,
        "refund_credit" => LedgerKind.RefundCredit,
        "payout" => LedgerKind.Payout,
        _ => LedgerKind.Unknown,
    };

    public static PayoutStatus ToPayoutStatus(string? wire) => wire switch
    {
        "requested" => PayoutStatus.Requested,
        "paid" => PayoutStatus.Paid,
        "declined" => PayoutStatus.Declined,
        _ => PayoutStatus.Unknown,
    };

    public static string ToWire(PayoutMethod method) => method switch
    {
        PayoutMethod.Maya => "maya",
        _ => "gcash",
    };

    public static PayoutMethod ToPayoutMethod(string? wire) => wire switch
    {
        "maya" => PayoutMethod.Maya,
        "gcash" => PayoutMethod.GCash,
        _ => PayoutMethod.Unknown,
    };

    public static PaymentStatus ToPaymentStatus(string? wire) => wire switch
    {
        "awaiting_payment" => PaymentStatus.AwaitingPayment,
        "paid" => PaymentStatus.Paid,
        "failed" => PaymentStatus.Failed,
        "expired" => PaymentStatus.Expired,
        _ => PaymentStatus.AwaitingPayment,
    };
}

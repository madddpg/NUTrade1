namespace NUTrade1.Core;

/// <summary>
/// Where a commitment deposit is in its life. Mirrors DEPOSIT_STATUS in
/// functions/src/constants.ts.
/// </summary>
public enum DepositStatus
{
    Unknown = 0,

    /// <summary>QR issued, waiting for the money to land. The bid does not exist yet.</summary>
    AwaitingPayment,

    /// <summary>Paid and held against a live bid.</summary>
    LockedInEscrow,

    /// <summary>Older handovers credited the seller. New handovers return the bond to the bidder.</summary>
    CreditedToSeller,

    /// <summary>Returned to the bidder — outbid, lost, declined, withdrawn or cancelled.</summary>
    RefundedToBuyer,

    /// <summary>The winning bidder never turned up. The only way to lose a deposit.</summary>
    Forfeited,

    /// <summary>The QR lapsed unpaid, so the bid it was for never went live.</summary>
    Expired,
}

/// <summary>
/// A bid and the deposit standing behind it.
///
/// A bid does not exist until its deposit is paid: <c>requestBid</c> hands back one of
/// these with a QR code, and only once PayMongo confirms the money does the bid appear on
/// the listing. Keeping both amounts together is the point — a student needs to see that
/// they are scanning for <see cref="DepositDisplay"/>, not for their whole bid.
/// </summary>
public sealed class BidDeposit
{
    public string Id { get; set; } = string.Empty;
    public string ListingId { get; set; } = string.Empty;
    public string ListingTitle { get; set; } = string.Empty;

    /// <summary>The bid this deposit is for.</summary>
    public long AmountCentavos { get; set; }

    /// <summary>What has to be scanned now — a percentage of the bid.</summary>
    public long DepositCentavos { get; set; }

    public int DepositPercent { get; set; }

    /// <summary>Bid credit already reserved against this deposit. Zero when the student had none.</summary>
    public long CreditAppliedCentavos { get; set; }

    /// <summary>What the QR is for. The full deposit when no credit was applied. Zero when credit covered it.</summary>
    public long QrDueCentavos { get; set; }

    /// <summary>The bid went live without a QR because bid credit covered the deposit.</summary>
    public bool CoveredByCredit { get; set; }

    public DepositStatus Status { get; set; } = DepositStatus.Unknown;

    public string? QrImageUrl { get; set; }
    public string? QrImageBase64 { get; set; }
    public string? QrPayload { get; set; }
    public DateTimeOffset? QrExpiresAt { get; set; }

    /// <summary>Set once the deposit is paid and the bid has been written.</summary>
    public string? CommittedBidId { get; set; }

    /// <summary>Why it went back, when it did. See REFUND_REASON in the Functions.</summary>
    public string? RefundReason { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public string AmountDisplay => Money.ToDisplay(AmountCentavos);
    public string DepositDisplay => Money.ToDisplay(DepositCentavos);

    /// <summary>The amount on the QR. Falls back to the full deposit for intents written before credit.</summary>
    public string QrDueDisplay => Money.ToDisplay(QrDueCentavos > 0 ? QrDueCentavos : DepositCentavos);

    public string CreditAppliedDisplay => Money.ToDisplay(CreditAppliedCentavos);

    /// <summary>True once the bid is actually live on the listing.</summary>
    public bool IsCommitted => Status == DepositStatus.LockedInEscrow;

    /// <summary>Still waiting on the student to scan.</summary>
    public bool IsAwaitingPayment => Status == DepositStatus.AwaitingPayment;

    /// <summary>Nothing more will happen to this deposit.</summary>
    public bool IsResolved => Status is DepositStatus.CreditedToSeller
        or DepositStatus.RefundedToBuyer
        or DepositStatus.Forfeited
        or DepositStatus.Expired;

    public bool HasQrImage => !string.IsNullOrEmpty(QrImageUrl);

    public string StatusDisplay => Status switch
    {
        DepositStatus.AwaitingPayment => "Waiting for your deposit",
        DepositStatus.LockedInEscrow => "Deposit held — your bid is live",
        DepositStatus.CreditedToSeller => "Released to the seller",
        DepositStatus.RefundedToBuyer => RefundDisplay,
        DepositStatus.Forfeited => "You didn't show — the seller received this as bid credit",
        DepositStatus.Expired => "The code expired before it was paid",
        _ => "Unknown",
    };

    private string RefundDisplay => RefundReason switch
    {
        "outbid" => "Outbid — back as bid credit",
        "auction_lost" => "Auction lost — back as bid credit",
        "seller_cancelled" => "Seller cancelled — back as bid credit",
        "trade_completed" => "You showed up — back as bid credit",
        "withdrawn" => "Bid withdrawn — back as bid credit",
        "no_show_dismissed" => "Dispute resolved in your favour — back as bid credit",
        _ => "Back as bid credit",
    };
}

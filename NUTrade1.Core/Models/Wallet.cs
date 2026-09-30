namespace NUTrade1.Core;

/// <summary>Why a <see cref="LedgerEntry"/> exists. Mirrors LEDGER_KIND in functions/src/wallet.ts.</summary>
public enum LedgerKind
{
    Unknown = 0,

    /// <summary>A buyer's commitment deposit credited after a confirmed handover.</summary>
    DepositCredit,

    /// <summary>A deposit forfeited by a no-show buyer.</summary>
    Forfeit,

    /// <summary>In-app credit — a seller cancelled, or a payout was declined.</summary>
    RefundCredit,

    /// <summary>Balance sent off-platform. Always negative.</summary>
    Payout,
}

public enum PayoutStatus
{
    Unknown = 0,
    Requested,
    Paid,
    Declined,
}

public enum PayoutMethod
{
    Unknown = 0,
    GCash,
    Maya,
}

/// <summary>
/// A student's spendable balance. "Spendable" is exact: raising a payout request debits
/// it straight away, so this is never money that is already promised to somebody.
/// </summary>
public sealed class Wallet
{
    public string Uid { get; set; } = string.Empty;
    public long BalanceCentavos { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public string BalanceDisplay => Money.ToDisplay(BalanceCentavos);
}

/// <summary>One line of the append-only record behind <see cref="Wallet.BalanceCentavos"/>.</summary>
public sealed class LedgerEntry
{
    public string Id { get; set; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public LedgerKind Kind { get; set; } = LedgerKind.Unknown;

    /// <summary>Negative for anything leaving the wallet.</summary>
    public long AmountCentavos { get; set; }

    public string? ListingId { get; set; }
    public string? BidId { get; set; }
    public string? PayoutRequestId { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public bool IsCredit => AmountCentavos >= 0;

    /// <summary>Signed, so a row reads as "+₱120.00" or "−₱500.00" without further work.</summary>
    public string AmountDisplay =>
        (IsCredit ? "+" : "−") + Money.ToDisplay(Math.Abs(AmountCentavos));

    public string KindDisplay => Kind switch
    {
        LedgerKind.DepositCredit => "Deposit received",
        LedgerKind.Forfeit => "Deposit forfeited",
        LedgerKind.RefundCredit => "Credit added",
        LedgerKind.Payout => "Cashed out",
        _ => "Adjustment",
    };
}

/// <summary>A request to send the balance to GCash or Maya, settled by an admin by hand.</summary>
public sealed class PayoutRequest
{
    public string Id { get; set; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public long AmountCentavos { get; set; }
    public PayoutMethod Method { get; set; } = PayoutMethod.Unknown;
    public string AccountName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public PayoutStatus Status { get; set; } = PayoutStatus.Unknown;
    public string? DeclineReason { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public string AmountDisplay => Money.ToDisplay(AmountCentavos);
    public bool IsPending => Status == PayoutStatus.Requested;

    public string StatusDisplay => Status switch
    {
        PayoutStatus.Requested => "Waiting on an admin",
        PayoutStatus.Paid => "Sent",
        PayoutStatus.Declined => "Declined",
        _ => "Unknown",
    };
}

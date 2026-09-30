using NUTrade1.Core;
using Xunit;

namespace NUTrade1.Tests;

/// <summary>
/// The display rules behind the wallet. The balance itself is only ever moved by a Cloud
/// Function inside a transaction, so what is worth pinning down here is how a row reads:
/// a student who cannot tell a credit from a debit at a glance has no audit trail.
/// </summary>
public class WalletTests
{
    [Theory]
    [InlineData(15_000, true, "+₱150.00")]
    [InlineData(0, true, "+₱0.00")]
    [InlineData(-9_950, false, "−₱99.50")]
    public void LedgerEntry_signs_the_amount(long centavos, bool expectedCredit, string expectedDisplay)
    {
        var entry = new LedgerEntry { AmountCentavos = centavos };

        Assert.Equal(expectedCredit, entry.IsCredit);
        Assert.Equal(expectedDisplay, entry.AmountDisplay);
    }

    // A payout is the one row that always leaves the wallet, so it must never read as a
    // credit however it was written.
    [Fact]
    public void Payout_row_reads_as_a_debit()
    {
        var entry = new LedgerEntry { Kind = LedgerKind.Payout, AmountCentavos = -24_500 };

        Assert.False(entry.IsCredit);
        Assert.StartsWith("−", entry.AmountDisplay);
        Assert.Equal("Cashed out", entry.KindDisplay);
    }

    [Theory]
    [InlineData(LedgerKind.DepositCredit, "Deposit received")]
    [InlineData(LedgerKind.Forfeit, "Deposit forfeited")]
    [InlineData(LedgerKind.RefundCredit, "Credit added")]
    [InlineData(LedgerKind.Unknown, "Adjustment")]
    public void LedgerEntry_names_every_kind(LedgerKind kind, string expected)
    {
        Assert.Equal(expected, new LedgerEntry { Kind = kind }.KindDisplay);
    }

    // "Waiting on an admin" is the only state where the money is held but not yet gone,
    // and it is the only one the student can still be told something about.
    [Theory]
    [InlineData(PayoutStatus.Requested, true, "Waiting on an admin")]
    [InlineData(PayoutStatus.Paid, false, "Sent")]
    [InlineData(PayoutStatus.Declined, false, "Declined")]
    public void PayoutRequest_reports_its_state(PayoutStatus status, bool expectedPending, string expectedDisplay)
    {
        var request = new PayoutRequest { Status = status };

        Assert.Equal(expectedPending, request.IsPending);
        Assert.Equal(expectedDisplay, request.StatusDisplay);
    }

    [Fact]
    public void Wallet_with_no_document_reads_as_zero()
    {
        Assert.Equal("₱0.00", new Wallet().BalanceDisplay);
    }
}

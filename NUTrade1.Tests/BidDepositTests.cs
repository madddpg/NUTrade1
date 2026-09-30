using NUTrade1.Core;
using Xunit;

namespace NUTrade1.Tests;

/// <summary>
/// How a deposit reads to the student. The state machine itself is server-side, so what
/// matters here is that the app never tells someone their bid is live when it isn't, and
/// never leaves a resolved deposit looking like it is still waiting.
/// </summary>
public class BidDepositTests
{
    [Fact]
    public void A_deposit_awaiting_payment_is_not_a_bid()
    {
        var deposit = new BidDeposit { Status = DepositStatus.AwaitingPayment };

        Assert.True(deposit.IsAwaitingPayment);
        Assert.False(deposit.IsCommitted);
        Assert.False(deposit.IsResolved);
    }

    [Fact]
    public void Only_locked_in_escrow_means_the_bid_is_live()
    {
        var deposit = new BidDeposit { Status = DepositStatus.LockedInEscrow };

        Assert.True(deposit.IsCommitted);
        Assert.False(deposit.IsAwaitingPayment);
        // Held is not resolved — the auction still has to play out.
        Assert.False(deposit.IsResolved);
    }

    [Theory]
    [InlineData(DepositStatus.CreditedToSeller)]
    [InlineData(DepositStatus.RefundedToBuyer)]
    [InlineData(DepositStatus.Forfeited)]
    [InlineData(DepositStatus.Expired)]
    public void Every_terminal_state_reports_itself_resolved(DepositStatus status)
    {
        var deposit = new BidDeposit { Status = status };

        Assert.True(deposit.IsResolved);
        Assert.False(deposit.IsCommitted);
        Assert.False(deposit.IsAwaitingPayment);
    }

    // The reason is what turns "deposit returned" into something a student can act on,
    // and every reason the Functions can send has to have a phrasing here.
    [Theory]
    [InlineData("outbid", "Outbid — deposit returned")]
    [InlineData("auction_lost", "Auction lost — deposit returned")]
    [InlineData("seller_cancelled", "Seller cancelled — deposit returned")]
    [InlineData("withdrawn", "Bid withdrawn — deposit returned")]
    [InlineData("no_show_dismissed", "Dispute resolved in your favour — deposit returned")]
    [InlineData(null, "Deposit returned")]
    [InlineData("something_new", "Deposit returned")]
    public void A_returned_deposit_explains_why(string? reason, string expected)
    {
        var deposit = new BidDeposit { Status = DepositStatus.RefundedToBuyer, RefundReason = reason };

        Assert.Equal(expected, deposit.StatusDisplay);
    }

    [Fact]
    public void Forfeited_says_plainly_what_happened()
    {
        var deposit = new BidDeposit { Status = DepositStatus.Forfeited };

        Assert.Equal("Deposit forfeited — you didn't show up", deposit.StatusDisplay);
    }

    // The deposit and the bid are different numbers and the screen shows both; mixing
    // them up would have a student scanning for the wrong amount.
    [Fact]
    public void The_deposit_and_the_bid_are_reported_separately()
    {
        var deposit = new BidDeposit { AmountCentavos = 50_000, DepositCentavos = 7_500 };

        Assert.Equal("₱500.00", deposit.AmountDisplay);
        Assert.Equal("₱75.00", deposit.DepositDisplay);
    }

    [Fact]
    public void A_deposit_with_no_qr_image_says_so()
    {
        Assert.False(new BidDeposit().HasQrImage);
        Assert.True(new BidDeposit { QrImageUrl = "https://example.test/qr.png" }.HasQrImage);
    }
}

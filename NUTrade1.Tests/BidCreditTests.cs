using NUTrade1.Core;
using Xunit;

namespace NUTrade1.Tests;

public class BidCreditTests
{
    [Fact]
    public void A_deposit_is_fifteen_percent_of_the_bid()
    {
        Assert.Equal(7_500, BidCredit.DepositFor(50_000, 15));
    }

    [Fact]
    public void Credit_that_covers_the_deposit_needs_no_qr()
    {
        var (applied, qrDue) = BidCredit.Split(balanceCentavos: 10_000, depositCentavos: 7_500);

        Assert.Equal(7_500, applied);
        Assert.Equal(0, qrDue);
    }

    [Fact]
    public void A_short_balance_pays_what_it_can_and_leaves_the_rest_for_the_qr()
    {
        var (applied, qrDue) = BidCredit.Split(balanceCentavos: 2_000, depositCentavos: 7_500);

        Assert.Equal(2_000, applied);
        Assert.Equal(5_500, qrDue);
    }

    [Fact]
    public void No_credit_means_the_whole_deposit_is_scanned()
    {
        var (applied, qrDue) = BidCredit.Split(0, 7_500);

        Assert.Equal(0, applied);
        Assert.Equal(7_500, qrDue);
    }
}

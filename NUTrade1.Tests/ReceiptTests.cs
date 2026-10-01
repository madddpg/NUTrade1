using NUTrade1.Core;
using Xunit;

namespace NUTrade1.Tests;

public class ReceiptTests
{
    private static readonly DateTimeOffset When = new(2026, 10, 1, 15, 54, 0, TimeSpan.Zero);

    [Fact]
    public void A_listing_fee_receipt_names_the_charge_and_says_it_is_one_time()
    {
        var text = Receipts.ListingFee("listing-1", "Additional", 1_000, When).ToText();

        Assert.Contains("Listing fee", text);
        Assert.Contains("Reference: listing-1", text);
        Assert.Contains("When: 2026-10-01 15:54 UTC", text);
        Assert.Contains("Amount: ₱10.00", text);
        Assert.Contains("Package: Additional", text);
        Assert.Contains("One-time posting fee", text);
        Assert.Contains("no subscription", text);
    }

    [Fact]
    public void A_free_listing_receipt_says_there_was_no_charge()
    {
        var text = Receipts.ListingFee("listing-1", "Free", 0, When).ToText();

        Assert.Contains("Amount: ₱0.00", text);
        Assert.Contains("No charge", text);
    }

    [Fact]
    public void A_bid_deposit_receipt_keeps_the_deposit_separate_from_the_bid()
    {
        var text = Receipts.BidDeposit("intent-9", "Calculus book", 7_500, 50_000, When).ToText();

        Assert.Contains("Amount: ₱75.00", text);
        Assert.Contains("Bid: ₱500.00", text);
        Assert.Contains("Listing: Calculus book", text);
        Assert.Contains("full price in person", text);
    }

    [Fact]
    public void A_file_name_is_safe_to_save()
    {
        var receipt = Receipts.Handover("chat/1", "Lamp", 20_000, When);

        Assert.Equal("nutrade-receipt-chat-1", receipt.FileName);
        Assert.DoesNotContain("/", receipt.FileName);
    }
}

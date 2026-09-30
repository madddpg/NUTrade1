using NUTrade1.Core;
using Xunit;

namespace NUTrade1.Tests;

/// <summary>
/// Who may bid. The rule that matters most is the first one: a seller can never bid on
/// their own listing, whatever else is true — it would let them bid their own price up.
/// </summary>
public class BidEligibilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static Listing LiveListing(string ownerUid = "seller") => new()
    {
        Id = "l1",
        OwnerUid = ownerUid,
        Status = ListingStatus.Active,
        AuctionEndsAt = Now.AddHours(3),
    };

    [Fact]
    public void A_verified_student_may_bid_on_someone_elses_live_auction()
    {
        Assert.Null(BidEligibility.WhyNot(LiveListing(), "buyer", isVerified: true, Now));
    }

    [Fact]
    public void The_seller_can_never_bid_on_their_own_listing()
    {
        Assert.Equal(BidEligibility.OwnListing,
            BidEligibility.WhyNot(LiveListing("seller"), "seller", isVerified: true, Now));
    }

    // Ownership is reported ahead of everything else: telling a seller to verify their
    // email, or that the auction ended, would suggest they could bid once that changed.
    [Theory]
    [InlineData(false, ListingStatus.Active)]
    [InlineData(true, ListingStatus.Matched)]
    [InlineData(false, ListingStatus.PendingApproval)]
    public void Ownership_is_the_reason_given_even_when_other_checks_also_fail(bool verified, ListingStatus status)
    {
        var listing = LiveListing("seller");
        listing.Status = status;

        Assert.Equal(BidEligibility.OwnListing, BidEligibility.WhyNot(listing, "seller", verified, Now));
    }

    [Fact]
    public void Ownership_compares_uids_exactly()
    {
        // Firebase uids are case-sensitive; a near-miss is a different account.
        Assert.Null(BidEligibility.WhyNot(LiveListing("Seller"), "seller", isVerified: true, Now));
        Assert.False(BidEligibility.IsSeller(LiveListing("Seller"), "seller"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_signed_out_student_is_asked_to_sign_in(string? uid)
    {
        Assert.Equal(BidEligibility.SignedOut, BidEligibility.WhyNot(LiveListing(), uid, isVerified: true, Now));
        Assert.False(BidEligibility.IsSeller(LiveListing(""), uid));
    }

    [Fact]
    public void An_unverified_student_is_asked_to_confirm_their_email()
    {
        Assert.Equal(BidEligibility.Unverified, BidEligibility.WhyNot(LiveListing(), "buyer", isVerified: false, Now));
    }

    [Theory]
    [InlineData(ListingStatus.Draft)]
    [InlineData(ListingStatus.PendingPayment)]
    [InlineData(ListingStatus.PendingApproval)]
    [InlineData(ListingStatus.Matched)]
    [InlineData(ListingStatus.Completed)]
    [InlineData(ListingStatus.Expired)]
    [InlineData(ListingStatus.Rejected)]
    public void Only_an_active_listing_takes_bids(ListingStatus status)
    {
        var listing = LiveListing();
        listing.Status = status;

        Assert.Equal(BidEligibility.NotAcceptingBids, BidEligibility.WhyNot(listing, "buyer", isVerified: true, Now));
    }

    [Fact]
    public void An_auction_past_its_end_time_takes_no_bids_even_before_the_sweep_closes_it()
    {
        var listing = LiveListing();
        listing.AuctionEndsAt = Now;

        Assert.Equal(BidEligibility.Ended, BidEligibility.WhyNot(listing, "buyer", isVerified: true, Now));
    }

    [Fact]
    public void A_listing_that_has_not_loaded_takes_no_bids()
    {
        Assert.Equal(BidEligibility.NotAcceptingBids, BidEligibility.WhyNot(null, "buyer", isVerified: true, Now));
    }
}

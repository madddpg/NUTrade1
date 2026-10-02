using NUTrade1.Core;
using Xunit;

namespace NUTrade1.Tests;

public class ListingKindRulesTests
{
    [Fact]
    public void An_auction_needs_a_starting_bid_and_an_increment()
    {
        var (start, increment, error) = ListingKindRules.Normalize(ListingKind.Auction, "150", "10");

        Assert.Null(error);
        Assert.Equal(15_000, start);
        Assert.Equal(1_000, increment);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("nope")]
    public void An_auction_rejects_a_missing_starting_bid(string? price)
    {
        var (_, _, error) = ListingKindRules.Normalize(ListingKind.Auction, price, "10");
        Assert.Equal("Enter a valid starting bid.", error);
    }

    [Fact]
    public void An_auction_rejects_a_missing_increment()
    {
        var (_, _, error) = ListingKindRules.Normalize(ListingKind.Auction, "150", "");
        Assert.Equal("Enter a valid bid increment.", error);
    }

    [Fact]
    public void A_standard_listing_stores_the_price_and_a_zero_increment()
    {
        var (start, increment, error) = ListingKindRules.Normalize(ListingKind.Standard, "80", "999");

        Assert.Null(error);
        Assert.Equal(8_000, start);
        Assert.Equal(0, increment);
    }

    [Fact]
    public void A_swap_stores_zeros_and_ignores_the_price_boxes()
    {
        var (start, increment, error) = ListingKindRules.Normalize(ListingKind.Swap, "80", "10");

        Assert.Null(error);
        Assert.Equal(0, start);
        Assert.Equal(0, increment);
    }
}

public class ListingKindDisplayTests
{
    [Fact]
    public void A_listing_with_no_kind_set_is_an_auction()
    {
        var listing = new Listing { AuctionEndsAt = DateTimeOffset.UtcNow.AddHours(1) };

        Assert.Equal(ListingKind.Auction, listing.Kind);
        Assert.True(listing.IsAuction);
        Assert.True(listing.HasAuctionClock);
        Assert.Equal("HIGHEST BID", listing.OfferCaption);
    }

    [Fact]
    public void A_set_price_does_not_grow_an_auction_clock_even_when_an_end_time_is_present()
    {
        var listing = new Listing
        {
            Kind = ListingKind.Standard,
            StartingBidCentavos = 5_000,
            AuctionEndsAt = DateTimeOffset.UtcNow.AddHours(1),
        };
        listing.TickCountdown(DateTimeOffset.UtcNow);

        Assert.False(listing.HasAuctionClock);
        Assert.Equal(string.Empty, listing.CountdownText);
        Assert.Equal("PRICE", listing.OfferCaption);
        Assert.Equal(Money.ToDisplay(5_000), listing.OfferDisplay);
        Assert.Equal("Buy", listing.KindLabel);
    }

    [Fact]
    public void A_swap_offers_no_price()
    {
        var listing = new Listing { Kind = ListingKind.Swap };

        Assert.Equal("SWAP", listing.OfferCaption);
        Assert.Equal("Open", listing.OfferDisplay);
        Assert.Equal("Swap", listing.KindLabel);
        Assert.Contains("swap", listing.BuyerNote, StringComparison.OrdinalIgnoreCase);
    }
}

public class ListingKindBidTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ListingKind.Standard)]
    [InlineData(ListingKind.Swap)]
    public void Only_auctions_take_bids(ListingKind kind)
    {
        var listing = new Listing
        {
            OwnerUid = "seller",
            Status = ListingStatus.Active,
            Kind = kind,
            AuctionEndsAt = Now.AddHours(2),
        };

        Assert.Equal(BidEligibility.NotAnAuction,
            BidEligibility.WhyNot(listing, "buyer", isVerified: true, Now));
    }

    [Fact]
    public void The_seller_is_still_told_it_is_their_listing_before_the_kind_check()
    {
        var listing = new Listing
        {
            OwnerUid = "seller",
            Status = ListingStatus.Active,
            Kind = ListingKind.Swap,
        };

        Assert.Equal(BidEligibility.OwnListing,
            BidEligibility.WhyNot(listing, "seller", isVerified: true, Now));
    }
}

public class LocalCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_snapshot_just_inside_an_hour_is_fresh()
    {
        Assert.True(LocalCachePolicy.IsFresh(Now.AddMinutes(-59), Now));
    }

    [Fact]
    public void A_snapshot_an_hour_old_is_due_for_a_refresh()
    {
        Assert.False(LocalCachePolicy.IsFresh(Now.AddMinutes(-60), Now));
    }

    [Fact]
    public void A_future_timestamp_is_not_treated_as_fresh()
    {
        Assert.False(LocalCachePolicy.IsFresh(Now.AddMinutes(1), Now));
    }

    [Fact]
    public async Task Feed_and_profile_round_trip_through_the_file_cache()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nutrade-cache-" + Guid.NewGuid().ToString("N"));
        var cache = new JsonFileCache(directory);
        var ends = Now.AddHours(3);

        await cache.WriteFeedAsync(new FeedSnapshot
        {
            SavedAt = Now,
            OwnerUid = "ada",
            CategoryKey = "Textbooks",
            NextCursor = "20",
            Items =
            [
                CachedListing.From(new Listing
                {
                    Id = "l1",
                    Title = "Calculus",
                    Kind = ListingKind.Standard,
                    Category = ItemCategory.Textbooks,
                    StartingBidCentavos = 12_000,
                    Status = ListingStatus.Active,
                    IsVisible = true,
                    AuctionEndsAt = ends,
                }),
            ],
        });

        var feed = await cache.ReadFeedAsync("ada", "Textbooks");
        Assert.NotNull(feed);
        Assert.Equal("ada", feed.OwnerUid);
        Assert.Equal(Now, feed.SavedAt);
        Assert.Equal("20", feed.NextCursor);
        var listing = Assert.Single(feed.Items).ToListing();
        Assert.Equal("Calculus", listing.Title);
        Assert.Equal(ListingKind.Standard, listing.Kind);
        Assert.Equal(12_000, listing.StartingBidCentavos);
        Assert.False(listing.HasAuctionClock);

        await cache.WriteProfileAsync(new ProfileSnapshot
        {
            SavedAt = Now,
            Profile = new UserProfile { Uid = "abc", DisplayName = "Ada", Program = "SACE" },
        });

        var profile = await cache.ReadProfileAsync("abc");
        Assert.NotNull(profile);
        Assert.Equal("Ada", profile.Profile.DisplayName);
        Assert.Equal("SACE", profile.Profile.Program);

        Assert.Null(await cache.ReadFeedAsync("ada", ""));
        Assert.Null(await cache.ReadFeedAsync("grace", "Textbooks"));
    }
}

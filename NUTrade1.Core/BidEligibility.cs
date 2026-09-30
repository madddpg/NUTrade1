namespace NUTrade1.Core;

/// <summary>
/// Whether a student may bid on a listing, and if not, the reason in words they can act on.
///
/// This is the client's copy of the checks `requestBid` makes before it mints a deposit QR.
/// The server is the authority — a seller who got past this would still be refused there,
/// and again in `commitDeposit` — but asking here first means nobody is ever shown a bid
/// form, or a deposit screen, for a bid that was never going to be accepted.
/// </summary>
public static class BidEligibility
{
    public const string OwnListing = "You can't bid on your own listing.";
    public const string SignedOut = "Sign in to place a bid.";
    public const string Unverified = "Confirm your email before bidding.";
    public const string NotAnAuction = "Only auctions take bids.";
    public const string NotAcceptingBids = "This auction isn't accepting bids.";
    public const string Ended = "This auction has already ended.";

    /// <summary>
    /// Null when <paramref name="uid"/> may bid on <paramref name="listing"/> right now;
    /// otherwise the reason they may not. Ownership is checked first, because it is the
    /// one refusal no amount of waiting or verifying will ever change.
    /// </summary>
    public static string? WhyNot(Listing? listing, string? uid, bool isVerified, DateTimeOffset now)
    {
        if (listing is null) return NotAcceptingBids;
        if (string.IsNullOrEmpty(uid)) return SignedOut;
        if (IsSeller(listing, uid)) return OwnListing;
        if (!isVerified) return Unverified;
        if (listing.Kind != ListingKind.Auction) return NotAnAuction;
        if (listing.Status != ListingStatus.Active) return NotAcceptingBids;
        if (listing.AuctionEndsAt is { } ends && ends <= now) return Ended;
        return null;
    }

    /// <summary>True when <paramref name="uid"/> posted <paramref name="listing"/>.</summary>
    public static bool IsSeller(Listing? listing, string? uid) =>
        listing is not null
        && !string.IsNullOrEmpty(uid)
        && string.Equals(listing.OwnerUid, uid, StringComparison.Ordinal);
}

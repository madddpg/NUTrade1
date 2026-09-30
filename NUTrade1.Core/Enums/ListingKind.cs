namespace NUTrade1.Core;

/// <summary>
/// How a listing is offered. Listings written before this field existed are auctions:
/// readers treat a missing value as <see cref="Auction"/>.
/// </summary>
public enum ListingKind
{
    /// <summary>24-hour auction. Bids require the commitment deposit.</summary>
    Auction,

    /// <summary>A set price. Buyers meet the seller; there is no bid.</summary>
    Standard,

    /// <summary>Offered for a swap. No price and no bid.</summary>
    Swap,
}

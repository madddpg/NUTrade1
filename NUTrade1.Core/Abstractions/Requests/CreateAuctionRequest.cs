namespace NUTrade1.Core;

/// <summary>
/// Everything the Create Auction form gathers before a draft listing is written.
/// No duration: every auction runs <see cref="NUTradeConstants.AuctionDurationHours"/>.
/// </summary>
public sealed class CreateAuctionRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ItemCondition Condition { get; set; } = ItemCondition.Unknown;
    public ItemCategory Category { get; set; } = ItemCategory.Unknown;

    /// <summary>What the seller typed when <see cref="Category"/> is <c>Other</c>; empty otherwise.</summary>
    public string CategoryOther { get; set; } = string.Empty;

    /// <summary>Local file paths of the picked photos, uploaded to Storage by the service.</summary>
    public IList<string> PhotoLocalPaths { get; set; } = new List<string>();

    public CampusZone CampusZone { get; set; } = CampusZone.Unknown;

    /// <summary>What the seller typed when <see cref="CampusZone"/> is <c>Other</c>; empty otherwise.</summary>
    public string CampusZoneOther { get; set; } = string.Empty;
    public ListingPackage Package { get; set; } = ListingPackage.Free;

    public long StartingBidCentavos { get; set; }
    public long MinIncrementCentavos { get; set; }
    public long? ReservePriceCentavos { get; set; }
}

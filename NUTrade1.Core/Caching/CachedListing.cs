namespace NUTrade1.Core;

/// <summary>
/// A listing reduced to the fields the feed paints and the detail page can open from.
/// Kept separate from <see cref="Listing"/> so the cache file is not the UI object,
/// which raises change notifications and owns the live countdown text.
/// </summary>
public sealed class CachedListing
{
    public string Id { get; set; } = string.Empty;
    public string OwnerUid { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ItemCondition Condition { get; set; }
    public ItemCategory Category { get; set; }
    public string CategoryOther { get; set; } = string.Empty;
    public List<string> Photos { get; set; } = new();
    public CampusZone CampusZone { get; set; }
    public string CampusZoneOther { get; set; } = string.Empty;
    public ListingPackage Package { get; set; }
    public ListingKind Kind { get; set; } = ListingKind.Auction;
    public bool IsPinned { get; set; }
    public ListingStatus Status { get; set; }
    public long StartingBidCentavos { get; set; }
    public long MinIncrementCentavos { get; set; }
    public long? ReservePriceCentavos { get; set; }
    public bool IsVisible { get; set; }
    public DateTimeOffset? VisibleFrom { get; set; }
    public long CurrentHighestBidCentavos { get; set; }
    public string? HighestBidderUid { get; set; }
    public int BidCount { get; set; }
    public DateTimeOffset? AuctionEndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    public static CachedListing From(Listing listing) => new()
    {
        Id = listing.Id,
        OwnerUid = listing.OwnerUid,
        Title = listing.Title,
        Description = listing.Description,
        Condition = listing.Condition,
        Category = listing.Category,
        CategoryOther = listing.CategoryOther,
        Photos = listing.Photos.ToList(),
        CampusZone = listing.CampusZone,
        CampusZoneOther = listing.CampusZoneOther,
        Package = listing.Package,
        Kind = listing.Kind,
        IsPinned = listing.IsPinned,
        Status = listing.Status,
        StartingBidCentavos = listing.StartingBidCentavos,
        MinIncrementCentavos = listing.MinIncrementCentavos,
        ReservePriceCentavos = listing.ReservePriceCentavos,
        IsVisible = listing.IsVisible,
        VisibleFrom = listing.VisibleFrom,
        CurrentHighestBidCentavos = listing.CurrentHighestBidCentavos,
        HighestBidderUid = listing.HighestBidderUid,
        BidCount = listing.BidCount,
        AuctionEndsAt = listing.AuctionEndsAt,
        CreatedAt = listing.CreatedAt,
        PublishedAt = listing.PublishedAt,
    };

    public Listing ToListing()
    {
        var listing = new Listing
        {
            Id = Id,
            OwnerUid = OwnerUid,
            Title = Title,
            Description = Description,
            Condition = Condition,
            Category = Category,
            CategoryOther = CategoryOther,
            Photos = Photos.ToList(),
            CampusZone = CampusZone,
            CampusZoneOther = CampusZoneOther,
            Package = Package,
            Kind = Kind,
            IsPinned = IsPinned,
            Status = Status,
            StartingBidCentavos = StartingBidCentavos,
            MinIncrementCentavos = MinIncrementCentavos,
            ReservePriceCentavos = ReservePriceCentavos,
            IsVisible = IsVisible,
            VisibleFrom = VisibleFrom,
            CurrentHighestBidCentavos = CurrentHighestBidCentavos,
            HighestBidderUid = HighestBidderUid,
            BidCount = BidCount,
            AuctionEndsAt = AuctionEndsAt,
            CreatedAt = CreatedAt,
            PublishedAt = PublishedAt,
        };
        listing.TickCountdown(DateTimeOffset.UtcNow);
        return listing;
    }
}

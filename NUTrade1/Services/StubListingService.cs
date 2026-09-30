using NUTrade1.Core;

namespace NUTrade1.Services;

/// <summary>
/// Offline stand-in for <see cref="IListingService"/>. Starts empty — real auctions
/// live in Firestore; this exists only so the UI can be exercised with no network.
/// </summary>
public sealed class StubListingService : IListingService
{
    private readonly IAuthService _auth;
    private readonly List<Listing> _listings = new();

    public StubListingService(IAuthService auth) => _auth = auth;

    public Task<ListingPage> GetActiveFeedAsync(
        ItemCategory? category = null,
        int pageSize = 20,
        string? cursor = null,
        CancellationToken ct = default)
    {
        IEnumerable<Listing> query = _listings.Where(l => l.Status == ListingStatus.Active);
        if (category is { } c) query = query.Where(l => l.Category == c);

        var items = query
            .OrderByDescending(l => l.IsPinned)
            .ThenByDescending(l => l.PublishedAt ?? l.CreatedAt)
            .Take(pageSize)
            .ToArray();

        return Task.FromResult(new ListingPage(items, NextCursor: null));
    }

    public Task<Listing?> GetListingAsync(string listingId, CancellationToken ct = default) =>
        Task.FromResult(_listings.FirstOrDefault(l => l.Id == listingId));

    public Task<OperationResult<string>> CreateDraftAsync(
        CreateAuctionRequest request, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var listing = new Listing
        {
            Id = Guid.NewGuid().ToString("n"),
            OwnerUid = _auth.CurrentUid ?? string.Empty,
            Title = request.Title,
            Description = request.Description,
            Condition = request.Condition,
            Category = request.Category,
            CategoryOther = request.CategoryOther,
            Photos = request.PhotoLocalPaths.ToList(),
            CampusZone = request.CampusZone,
            CampusZoneOther = request.CampusZoneOther,
            Package = request.Package,
            StartingBidCentavos = request.StartingBidCentavos,
            MinIncrementCentavos = request.MinIncrementCentavos,
            ReservePriceCentavos = request.ReservePriceCentavos,
            CurrentHighestBidCentavos = request.StartingBidCentavos,
            Status = ListingStatus.Draft,
            CreatedAt = now,
        };
        _listings.Add(listing);
        return Task.FromResult(OperationResult<string>.Ok(listing.Id));
    }

    public Task<IReadOnlyList<Listing>> GetMyListingsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Listing>>(
            _listings.Where(l => l.OwnerUid == _auth.CurrentUid).ToArray());

    public Task<bool> HasUsedFreePostAsync(CancellationToken ct = default) =>
        Task.FromResult(_listings.Any(l => l.OwnerUid == _auth.CurrentUid && NUTradeConstants.UsesFreePost(l.Status)));

    public Task<OperationResult> CancelListingAsync(string listingId, CancellationToken ct = default)
    {
        if (_listings.FirstOrDefault(l => l.Id == listingId) is { } listing)
            listing.Status = ListingStatus.Cancelled;
        return Task.FromResult(OperationResult.Ok());
    }

    public IDisposable ObserveListing(string listingId, Action<Listing?> onChanged)
    {
        onChanged(_listings.FirstOrDefault(l => l.Id == listingId));
        return new NoopDisposable();
    }

    /// <summary>Stands in for what <c>createQrPayment</c> does server-side: the fee is
    /// settled and the listing joins the admin queue.</summary>
    internal void SubmitForApproval(string listingId, ListingPackage package)
    {
        if (_listings.FirstOrDefault(l => l.Id == listingId) is not { } listing) return;
        listing.Status = ListingStatus.PendingApproval;
        listing.PaidPackage = package;
        listing.SubmittedForApprovalAt = DateTimeOffset.UtcNow;
    }

    internal IReadOnlyList<Listing> PendingApproval() =>
        _listings.Where(l => l.Status == ListingStatus.PendingApproval)
                 .OrderBy(l => l.SubmittedForApprovalAt ?? l.CreatedAt)
                 .ToArray();

    /// <summary>Stands in for <c>approveListing</c>, so the offline post-an-auction flow
    /// ends with something visible on the feed.</summary>
    internal void Publish(string listingId)
    {
        if (_listings.FirstOrDefault(l => l.Id == listingId) is not { } listing) return;
        var now = DateTimeOffset.UtcNow;
        listing.Status = ListingStatus.Active;
        listing.PublishedAt = now;
        listing.IsPinned = listing.PaidPackage == ListingPackage.Priority;
        listing.IsVisible = true;
        listing.AuctionEndsAt = now.AddHours(NUTradeConstants.AuctionDurationHours);
    }

    /// <summary>Stands in for <c>rejectListing</c>.</summary>
    internal void Reject(string listingId, string? reason)
    {
        if (_listings.FirstOrDefault(l => l.Id == listingId) is not { } listing) return;
        listing.Status = ListingStatus.Rejected;
        listing.RejectionReason = reason;
    }

    /// <summary>Mirrors <c>placeBid</c>'s update of the denormalized highest-bid fields.</summary>
    internal void ApplyBid(string listingId, long amountCentavos, string bidderUid)
    {
        if (_listings.FirstOrDefault(l => l.Id == listingId) is not { } listing) return;
        listing.CurrentHighestBidCentavos = amountCentavos;
        listing.HighestBidderUid = bidderUid;
        listing.BidCount++;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

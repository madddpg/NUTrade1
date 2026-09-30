namespace NUTrade1.Core;

/// <summary>A page of feed results plus a cursor for the next page.</summary>
public sealed record ListingPage(IReadOnlyList<Listing> Items, string? NextCursor);

/// <summary>Feed queries and auction authoring. The client never sets <see cref="ListingStatus.Active"/>.</summary>
public interface IListingService
{
    /// <summary>
    /// One page of <see cref="ListingStatus.Active"/> listings, pinned-first, newest next.
    /// Pass the previous page's <see cref="ListingPage.NextCursor"/> to continue.
    /// </summary>
    Task<ListingPage> GetActiveFeedAsync(
        ItemCategory? category = null,
        int pageSize = 20,
        string? cursor = null,
        CancellationToken ct = default);

    Task<Listing?> GetListingAsync(string listingId, CancellationToken ct = default);

    /// <summary>Uploads photos, writes a draft/pending listing, and returns its id.</summary>
    Task<OperationResult<string>> CreateDraftAsync(CreateAuctionRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<Listing>> GetMyListingsAsync(CancellationToken ct = default);

    /// <summary>
    /// True once the signed-in student has used their one free post — see
    /// <see cref="NUTradeConstants.UsesFreePost"/>. Drives whether the Free package is offered.
    /// </summary>
    Task<bool> HasUsedFreePostAsync(CancellationToken ct = default);

    Task<OperationResult> CancelListingAsync(string listingId, CancellationToken ct = default);

    /// <summary>Subscribes to a single listing document. Dispose to detach the snapshot listener.</summary>
    IDisposable ObserveListing(string listingId, Action<Listing?> onChanged);
}

namespace NUTrade1.Core;

/// <summary>
/// The admin side of listing approval. Every new listing waits in
/// <see cref="ListingStatus.PendingApproval"/> after its fee clears; only an admin
/// can put it live or turn it down, and both decisions are made by Cloud Functions
/// that re-check the admin claim.
/// </summary>
public interface IListingModerationService
{
    /// <summary>Listings waiting for review, oldest submission first.</summary>
    Task<IReadOnlyList<Listing>> GetPendingListingsAsync(CancellationToken ct = default);

    /// <summary>Puts the listing live; its 24-hour auction starts at approval.</summary>
    Task<OperationResult> ApproveListingAsync(string listingId, CancellationToken ct = default);

    /// <summary>Turns the listing down. <paramref name="reason"/> is shown to the seller.</summary>
    Task<OperationResult> RejectListingAsync(string listingId, string? reason, CancellationToken ct = default);
}

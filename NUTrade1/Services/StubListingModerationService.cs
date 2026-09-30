using NUTrade1.Core;

namespace NUTrade1.Services;

/// <summary>
/// Offline stand-in for <see cref="IListingModerationService"/>, acting directly on
/// the in-memory <see cref="StubListingService"/>.
/// </summary>
public sealed class StubListingModerationService : IListingModerationService
{
    private readonly StubListingService? _listings;

    public StubListingModerationService(IListingService listings) => _listings = listings as StubListingService;

    public Task<IReadOnlyList<Listing>> GetPendingListingsAsync(CancellationToken ct = default) =>
        Task.FromResult(_listings?.PendingApproval() ?? Array.Empty<Listing>());

    public Task<OperationResult> ApproveListingAsync(string listingId, CancellationToken ct = default)
    {
        _listings?.Publish(listingId);
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> RejectListingAsync(string listingId, string? reason, CancellationToken ct = default)
    {
        _listings?.Reject(listingId, reason);
        return Task.FromResult(OperationResult.Ok());
    }
}

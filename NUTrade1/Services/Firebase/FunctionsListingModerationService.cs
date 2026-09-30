using NUTrade1.Core;

namespace NUTrade1.Services.Firebase;

/// <summary>
/// <see cref="IListingModerationService"/> over the <c>approveListing</c> /
/// <c>rejectListing</c> callables.
///
/// The queue itself is a plain Firestore read — listings are readable by any signed-in
/// student — but both decisions are Functions: firestore.rules forbids every client
/// update to a listing, and the Functions re-check the admin claim rather than trusting
/// that only admins can reach this screen.
/// </summary>
public sealed class FunctionsListingModerationService : IListingModerationService
{
    /// <summary>Plenty for a campus queue; beyond this the oldest are shown first anyway.</summary>
    private const int QueueLimit = 100;

    private readonly FirestoreClient _firestore;
    private readonly FunctionsClient _functions;

    public FunctionsListingModerationService(FirestoreClient firestore, FunctionsClient functions)
    {
        _firestore = firestore;
        _functions = functions;
    }

    public async Task<IReadOnlyList<Listing>> GetPendingListingsAsync(CancellationToken ct = default)
    {
        // Equality only, sorted here: an orderBy on a second field would need its own
        // composite index for a queue that never holds more than a handful of items.
        var query = Q.Build(
            Q.From("listings"),
            Q.Equal("status", Fs.Str(WireCodec.ToWire(ListingStatus.PendingApproval))),
            limit: QueueLimit);

        var documents = await _firestore.RunQueryAsync(string.Empty, query, ct);
        return documents
            .Select(FirestoreListingService.Map)
            .OrderBy(l => l.SubmittedForApprovalAt ?? l.CreatedAt)
            .ToArray();
    }

    public async Task<OperationResult> ApproveListingAsync(string listingId, CancellationToken ct = default)
    {
        var result = await _functions.CallAsync("approveListing", new { listingId }, ct);
        return result.Succeeded ? OperationResult.Ok() : OperationResult.Fail(result.Error!);
    }

    public async Task<OperationResult> RejectListingAsync(string listingId, string? reason, CancellationToken ct = default)
    {
        var result = await _functions.CallAsync("rejectListing", new { listingId, reason = reason ?? string.Empty }, ct);
        return result.Succeeded ? OperationResult.Ok() : OperationResult.Fail(result.Error!);
    }
}

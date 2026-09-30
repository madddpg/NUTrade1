using System.Text.Json;
using NUTrade1.Core;

namespace NUTrade1.Services.Firebase;

/// <summary>
/// <see cref="IBidService"/> split across both transports, and that split is the
/// whole point: reads come straight from the <c>listings/{id}/bids</c> subcollection,
/// while every mutation is a Cloud Function call. firestore.rules denies all client
/// writes to that subcollection, because a bid has to be validated against the current
/// highest bid inside a transaction — something a client can never do honestly.
/// </summary>
public sealed class FirestoreBidService : IBidService
{
    private readonly FirestoreClient _firestore;
    private readonly FunctionsClient _functions;
    private readonly IAuthService _auth;

    public FirestoreBidService(FirestoreClient firestore, FunctionsClient functions, IAuthService auth)
    {
        _firestore = firestore;
        _functions = functions;
        _auth = auth;
    }

    public async Task<IReadOnlyList<Bid>> GetBidsForListingAsync(
        string listingId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(listingId)) return Array.Empty<Bid>();

        var query = Q.Build(
            Q.From("bids"),
            orderBy: new[] { Q.OrderBy("createdAt", descending: true) },
            limit: 50);

        var documents = await _firestore.RunQueryAsync($"listings/{listingId}", query, ct);
        return documents.Select(d => Map(d, listingId)).ToArray();
    }

    public async Task<IReadOnlyList<Bid>> GetMyBidsAsync(CancellationToken ct = default)
    {
        if (_auth.CurrentUid is not { } uid) return Array.Empty<Bid>();

        // A collection-group query: my bids live one per listing, scattered across
        // every listings/{id}/bids subcollection.
        var query = Q.Build(
            Q.From("bids", allDescendants: true),
            Q.Equal("bidderUid", Fs.Str(uid)),
            new[] { Q.OrderBy("createdAt", descending: true) },
            limit: 50);

        var documents = await _firestore.RunQueryAsync(string.Empty, query, ct);
        return documents.Select(d => Map(d, null)).ToArray();
    }

    public async Task<IReadOnlyList<Bid>> GetIncomingBidsAsync(
        string listingId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(listingId)) return Array.Empty<Bid>();

        var query = Q.Build(
            Q.From("bids"),
            Q.Equal("status", FsLower.Enum(BidStatus.Pending)),
            new[] { Q.OrderBy("amountCentavos", descending: true) },
            limit: 20);

        var documents = await _firestore.RunQueryAsync($"listings/{listingId}", query, ct);
        return documents.Select(d => Map(d, listingId)).ToArray();
    }

    public Task<OperationResult> ApproveBidAsync(string listingId, string bidId, CancellationToken ct = default) =>
        CallBidActionAsync("approveBid", listingId, bidId, ct);

    public Task<OperationResult> DeclineBidAsync(string listingId, string bidId, CancellationToken ct = default) =>
        CallBidActionAsync("declineBid", listingId, bidId, ct);

    public Task<OperationResult> WithdrawBidAsync(string listingId, string bidId, CancellationToken ct = default) =>
        CallBidActionAsync("withdrawBid", listingId, bidId, ct);

    private async Task<OperationResult> CallBidActionAsync(
        string function, string listingId, string bidId, CancellationToken ct)
    {
        var result = await _functions.CallAsync(function, new { listingId, bidId }, ct);
        return result.Succeeded ? OperationResult.Ok() : OperationResult.Fail(result.Error!);
    }

    /// <summary>
    /// Maps a bid document. <paramref name="listingId"/> is supplied when the caller
    /// already knows it; a collection-group result instead carries it as a denormalized
    /// field, written by <c>placeBid</c> precisely so My Bids can join back to listings.
    /// </summary>
    internal static Bid Map(JsonElement document, string? listingId)
    {
        var fields = document.GetProperty("fields");
        return new Bid
        {
            Id = Fs.IdFromName(document),
            ListingId = listingId ?? Fs.StringOr(fields, "listingId"),
            BidderUid = Fs.StringOr(fields, "bidderUid"),
            BidderName = Fs.StringOr(fields, "bidderName", "NU student"),
            AmountCentavos = Fs.Long(fields, "amountCentavos"),
            Status = Fs.Enum(fields, "status", BidStatus.Pending),
            ChatId = Fs.String(fields, "chatId"),
            CreatedAt = Fs.Timestamp(fields, "createdAt") ?? DateTimeOffset.UtcNow,
        };
    }
}

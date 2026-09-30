using System.Globalization;
using System.Text.Json;
using NUTrade1.Core;

namespace NUTrade1.Services.Firebase;

/// <summary>
/// <see cref="IListingService"/> over <c>listings/{listingId}</c>.
///
/// Reads are direct Firestore queries; the one write this class makes is a *draft*.
/// Publishing, pinning and every auction counter belong to the Cloud Functions —
/// firestore.rules denies a client update outright. Settling the fee — through
/// <c>createQrPayment</c> (free first post) or the PayMongo webhook (paid post) — only
/// moves a listing to <see cref="ListingStatus.PendingApproval"/>; it reaches the feed
/// once an admin's <c>approveListing</c> has moved it to <see cref="ListingStatus.Active"/>.
/// </summary>
public sealed class FirestoreListingService : IListingService
{
    private readonly FirestoreClient _firestore;
    private readonly IStorageService _storage;
    private readonly IAuthService _auth;

    public FirestoreListingService(FirestoreClient firestore, IStorageService storage, IAuthService auth)
    {
        _firestore = firestore;
        _storage = storage;
        _auth = auth;
    }

    public async Task<ListingPage> GetActiveFeedAsync(
        ItemCategory? category = null,
        int pageSize = 20,
        string? cursor = null,
        CancellationToken ct = default)
    {
        var offset = int.TryParse(cursor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

        var filters = new List<Dictionary<string, object?>>
        {
            Q.Equal("status", Fs.Str(WireCodec.ToWire(ListingStatus.Active))),
            // Regular listings sit out of the feed until publishScheduledListings
            // releases them on the hour; Priority is published already visible.
            Q.Equal("isVisible", Fs.Bool(true)),
        };
        if (category is { } c)
            filters.Add(Q.Equal("category", Fs.Enum(c)));

        var query = Q.Build(
            Q.From("listings"),
            Q.And(filters.ToArray()),
            new[]
            {
                // Paid Priority pins ride at the top of the feed; everything else is newest-first.
                Q.OrderBy("isPinned", descending: true),
                Q.OrderBy("publishedAt", descending: true),
            },
            limit: pageSize,
            offset: offset);

        var documents = await _firestore.RunQueryAsync(string.Empty, query, ct);
        var items = documents.Select(Map).ToArray();

        // A short page means we reached the end — stop offering a cursor.
        var next = items.Length == pageSize ? (offset + items.Length).ToString(CultureInfo.InvariantCulture) : null;
        return new ListingPage(items, next);
    }

    public async Task<Listing?> GetListingAsync(string listingId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(listingId)) return null;
        var document = await _firestore.GetDocumentAsync($"listings/{listingId}", ct);
        return document is { } doc ? Map(doc) : null;
    }

    public async Task<OperationResult<string>> CreateDraftAsync(
        CreateAuctionRequest request, CancellationToken ct = default)
    {
        if (_auth.CurrentUid is not { } uid) return OperationResult<string>.Fail("Sign in to continue.");
        if (!_auth.IsVerified)
            return OperationResult<string>.Fail("Your student ID is still being reviewed — you can't post auctions yet.");

        // Photos go to Storage first: the listing document stores download URLs, and a
        // half-written draft is better than a draft pointing at files that never landed.
        var photoUrls = new List<string>();
        foreach (var localPath in request.PhotoLocalPaths)
        {
            var upload = await _storage.UploadAsync($"listings/{uid}", localPath, ct);
            if (!upload.Succeeded)
                return OperationResult<string>.Fail(upload.Error ?? "Could not upload your photos.");
            photoUrls.Add(upload.Value!);
        }

        var fields = new Dictionary<string, object?>
        {
            ["ownerUid"] = Fs.Str(uid),
            // The web admin panel shares this database and names the seller `sellerUid`;
            // its rules check that field on create. Written alongside ownerUid, which is
            // what the app and the Functions use.
            ["sellerUid"] = Fs.Str(uid),
            ["title"] = Fs.Str(request.Title),
            ["description"] = Fs.Str(request.Description),
            ["condition"] = Fs.Enum(request.Condition),
            ["category"] = Fs.Enum(request.Category),
            ["categoryOther"] = Fs.Str(request.CategoryOther),
            ["photos"] = Fs.Arr(photoUrls),
            ["campusZone"] = Fs.Enum(request.CampusZone),
            ["campusZoneOther"] = Fs.Str(request.CampusZoneOther),
            ["package"] = Fs.Enum(request.Package),
            ["startingBidCentavos"] = Fs.Int(request.StartingBidCentavos),
            ["minIncrementCentavos"] = Fs.Int(request.MinIncrementCentavos),
            ["reservePriceCentavos"] = Fs.Int(request.ReservePriceCentavos),
            ["currentHighestBidCentavos"] = Fs.Int(request.StartingBidCentavos),
            ["createdAt"] = Fs.Ts(DateTimeOffset.UtcNow),

            // Everything below is what firestore.rules pins on create. The server owns
            // all of it from here on; the client never writes these again.
            ["status"] = Fs.Str(WireCodec.ToWire(ListingStatus.Draft)),
            ["isPinned"] = Fs.Bool(false),
            ["isVisible"] = Fs.Bool(false),
            ["visibleFrom"] = Fs.Null(),
            ["bidCount"] = Fs.Int(0),
            ["highestBidderUid"] = Fs.Null(),
            ["winningBidId"] = Fs.Null(),
            ["auctionEndsAt"] = Fs.Null(),
            ["publishedAt"] = Fs.Null(),
        };

        try
        {
            var id = await _firestore.CreateDocumentAsync("listings", null, fields, ct);
            return OperationResult<string>.Ok(id);
        }
        catch (FirestoreException ex)
        {
            return OperationResult<string>.Fail(ex.Message);
        }
    }

    public async Task<IReadOnlyList<Listing>> GetMyListingsAsync(CancellationToken ct = default)
    {
        if (_auth.CurrentUid is not { } uid) return Array.Empty<Listing>();

        var query = Q.Build(
            Q.From("listings"),
            Q.Equal("ownerUid", Fs.Str(uid)),
            new[] { Q.OrderBy("createdAt", descending: true) },
            limit: 50);

        var documents = await _firestore.RunQueryAsync(string.Empty, query, ct);
        return documents.Select(Map).ToArray();
    }

    /// <summary>Every status that uses the free post, spelled as Firestore stores it.</summary>
    private static readonly Dictionary<string, object?>[] FreePostUsedStatuses =
        Enum.GetValues<ListingStatus>()
            .Where(NUTradeConstants.UsesFreePost)
            .Select(s => Fs.Str(WireCodec.ToWire(s)))
            .ToArray();

    public async Task<bool> HasUsedFreePostAsync(CancellationToken ct = default)
    {
        if (_auth.CurrentUid is not { } uid) return false;

        // The same question createQrPayment asks before it waives the fee: does this
        // student own any listing that was submitted and not rejected?
        var query = Q.Build(
            Q.From("listings"),
            Q.And(Q.Equal("ownerUid", Fs.Str(uid)), Q.In("status", FreePostUsedStatuses)),
            limit: 1);

        var documents = await _firestore.RunQueryAsync(string.Empty, query, ct);
        return documents.Count > 0;
    }

    public async Task<OperationResult> CancelListingAsync(string listingId, CancellationToken ct = default)
    {
        // Only an unpublished draft can be deleted client-side; rules forbid touching
        // anything else, and pulling a live auction out from under its bidders has to
        // be a server decision.
        var listing = await GetListingAsync(listingId, ct);
        if (listing is null) return OperationResult.Fail("That listing no longer exists.");
        if (listing.Status != ListingStatus.Draft)
            return OperationResult.Fail("A published auction can't be cancelled from the app.");

        try
        {
            await _firestore.DeleteAsync($"listings/{listingId}", ct);
            return OperationResult.Ok();
        }
        catch (FirestoreException ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    public IDisposable ObserveListing(string listingId, Action<Listing?> onChanged) =>
        new PollingObserver<Listing?>(
            read: ct => GetListingAsync(listingId, ct),
            // The payment screen is watching status (paid → approved → on the feed), and
            // the detail page for the bid to move; those are the only fields worth a repaint.
            signature: l => l is null
                ? "none"
                : $"{l.Status}|{l.IsVisible}|{l.CurrentHighestBidCentavos}|{l.BidCount}|{l.IsPinned}|{l.AuctionEndsAt:O}",
            onChanged: onChanged,
            interval: FirebaseSettings.ObservePollInterval);

    internal static Listing Map(JsonElement document)
    {
        var fields = document.GetProperty("fields");
        var listing = new Listing
        {
            Id = Fs.IdFromName(document),
            OwnerUid = Fs.StringOr(fields, "ownerUid"),
            Title = Fs.StringOr(fields, "title"),
            Description = Fs.StringOr(fields, "description"),
            Condition = Fs.Enum(fields, "condition", ItemCondition.Unknown),
            Category = Fs.Enum(fields, "category", ItemCategory.Unknown),
            CategoryOther = Fs.StringOr(fields, "categoryOther"),
            Photos = Fs.StringList(fields, "photos"),
            CampusZone = Fs.Enum(fields, "campusZone", CampusZone.Unknown),
            CampusZoneOther = Fs.StringOr(fields, "campusZoneOther"),
            Package = Fs.Enum(fields, "package", ListingPackage.Free),
            IsPinned = Fs.Bool(fields, "isPinned"),
            Status = WireCodec.ToListingStatus(Fs.String(fields, "status")),
            StartingBidCentavos = Fs.Long(fields, "startingBidCentavos"),
            MinIncrementCentavos = Fs.Long(fields, "minIncrementCentavos"),
            ReservePriceCentavos = Fs.LongOrNull(fields, "reservePriceCentavos"),
            IsVisible = Fs.Bool(fields, "isVisible"),
            VisibleFrom = Fs.Timestamp(fields, "visibleFrom"),
            CurrentHighestBidCentavos = Fs.Long(fields, "currentHighestBidCentavos"),
            HighestBidderUid = Fs.String(fields, "highestBidderUid"),
            BidCount = Fs.Int32(fields, "bidCount"),
            AuctionEndsAt = Fs.Timestamp(fields, "auctionEndsAt"),
            WinningBidId = Fs.String(fields, "winningBidId"),
            OrderId = Fs.String(fields, "orderId"),
            CreatedAt = Fs.Timestamp(fields, "createdAt") ?? DateTimeOffset.UtcNow,
            PublishedAt = Fs.Timestamp(fields, "publishedAt"),
            PaidPackage = Enum.TryParse<ListingPackage>(Fs.String(fields, "paidPackage"), out var paid) ? paid : null,
            SubmittedForApprovalAt = Fs.Timestamp(fields, "submittedForApprovalAt"),
            RejectionReason = Fs.String(fields, "rejectionReason"),
        };

        // Prime the countdown so a card paints with real text on its first frame,
        // rather than blank until the page's dispatcher timer's first tick.
        listing.TickCountdown(DateTimeOffset.UtcNow);
        return listing;
    }
}

using System.Text.Json;
using NUTrade1.Core;

namespace NUTrade1.Services.Firebase;

/// <summary>
/// <see cref="IOrderService"/> over <c>orders/{orderId}</c>.
///
/// Reads come straight from Firestore; every mutation is a Cloud Function, because
/// the order's status transitions are the sale itself. A client that could write
/// <c>status: 'paid'</c> could walk off with an item it never paid for, which is why
/// firestore.rules denies all client writes here.
/// </summary>
public sealed class FirestoreOrderService : IOrderService
{
    private readonly FirestoreClient _firestore;
    private readonly FunctionsClient _functions;
    private readonly IAuthService _auth;

    public FirestoreOrderService(FirestoreClient firestore, FunctionsClient functions, IAuthService auth)
    {
        _firestore = firestore;
        _functions = functions;
        _auth = auth;
    }

    public async Task<Order?> GetOrderAsync(string orderId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orderId)) return null;
        var document = await _firestore.GetDocumentAsync($"orders/{orderId}", ct);
        return document is { } doc ? Map(doc) : null;
    }

    public async Task<Order?> GetOrderForListingAsync(string listingId, CancellationToken ct = default)
    {
        if (_auth.CurrentUid is not { } uid || string.IsNullOrWhiteSpace(listingId)) return null;

        // Scoped by participant because that is what the read rule allows — an
        // unscoped listingId query would be rejected outright.
        var query = Q.Build(
            Q.From("orders"),
            Q.And(
                Q.ArrayContains("participantUids", Fs.Str(uid)),
                Q.Equal("listingId", Fs.Str(listingId))),
            new[] { Q.OrderBy("createdAt", descending: true) },
            limit: 1);

        var documents = await _firestore.RunQueryAsync(string.Empty, query, ct);
        return documents.Count == 0 ? null : Map(documents[0]);
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(CancellationToken ct = default)
    {
        if (_auth.CurrentUid is not { } uid) return Array.Empty<Order>();

        var query = Q.Build(
            Q.From("orders"),
            Q.ArrayContains("participantUids", Fs.Str(uid)),
            new[] { Q.OrderBy("createdAt", descending: true) },
            limit: 50);

        var documents = await _firestore.RunQueryAsync(string.Empty, query, ct);
        return documents.Select(Map).ToArray();
    }

    public async Task<OperationResult<Order>> CreateQrPaymentAsync(
        string orderId, CancellationToken ct = default)
    {
        var result = await _functions.CallAsync("createOrderQrPayment", new { orderId }, ct);
        if (!result.Succeeded) return OperationResult<Order>.Fail(result.Error!);

        // The callable returns the QR, but the order document is the source of truth
        // for status — re-read it so the screen never shows a code for an order the
        // webhook has already settled.
        var order = await GetOrderAsync(orderId, ct);
        return order is null
            ? OperationResult<Order>.Fail("That order no longer exists.")
            : OperationResult<Order>.Ok(order);
    }
    public IDisposable ObserveOrder(string orderId, Action<Order?> onChanged) =>
        new PollingObserver<Order?>(
            read: ct => GetOrderAsync(orderId, ct),
            // Status and method are the only things the screen reacts to; the rest of
            // the order never changes after creation.
            signature: o => o is null ? "none" : $"{o.Status}|{o.QrExpiresAt:O}|{o.QrImageUrl?.Length}|{o.TestMode}",
            onChanged: onChanged,
            interval: FirebaseSettings.ObservePollInterval);

    internal static OrderStatus StatusFromWire(string? wire) => wire switch
    {
        "awaiting_payment" => OrderStatus.AwaitingPayment,
        "paid" => OrderStatus.Paid,
        "released" => OrderStatus.Released,
        "cancelled" => OrderStatus.Cancelled,
        _ => OrderStatus.AwaitingPayment,
    };

    internal static Order Map(JsonElement document)
    {
        var fields = document.GetProperty("fields");
        return new Order
        {
            Id = Fs.IdFromName(document),
            ListingId = Fs.StringOr(fields, "listingId"),
            ListingTitle = Fs.StringOr(fields, "listingTitle"),
            ListingPhoto = Fs.String(fields, "listingPhoto"),
            BidId = Fs.StringOr(fields, "bidId"),
            ChatId = Fs.StringOr(fields, "chatId"),
            SellerUid = Fs.StringOr(fields, "sellerUid"),
            BuyerUid = Fs.StringOr(fields, "buyerUid"),
            BuyerName = Fs.StringOr(fields, "buyerName"),
            AmountCentavos = Fs.Long(fields, "amountCentavos"),
            Reference = Fs.StringOr(fields, "reference"),
            Status = StatusFromWire(Fs.String(fields, "status")),
            DueAt = Fs.Timestamp(fields, "dueAt"),
            QrImageUrl = Fs.String(fields, "qrImageUrl"),
            QrImageBase64 = Fs.String(fields, "qrImageBase64"),
            QrPayload = Fs.String(fields, "qrPayload"),
            QrExpiresAt = Fs.Timestamp(fields, "qrExpiresAt"),
            TestMode = Fs.Bool(fields, "paymongoTestMode"),
            CreatedAt = Fs.Timestamp(fields, "createdAt") ?? DateTimeOffset.UtcNow,
        };
    }
}

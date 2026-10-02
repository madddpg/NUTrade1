using System.Text.Json;
using NUTrade1.Core;

namespace NUTrade1.Services.Firebase;

/// <summary>
/// <see cref="IPaymentService"/> against the <c>createQrPayment</c> callable.
///
/// The app never touches PayMongo. It asks the Function for a QR, renders it, and
/// then watches its own listing document: the money's arrival is reported by
/// PayMongo to <c>paymongoWebhook</c>, which is what actually publishes the auction.
/// Nothing the client does can shortcut that.
/// </summary>
public sealed class FunctionsPaymentService : IPaymentService
{
    private readonly FunctionsClient _functions;
    private readonly FirestoreClient _firestore;
    private readonly IAuthService _auth;

    public FunctionsPaymentService(FunctionsClient functions, FirestoreClient firestore, IAuthService auth)
    {
        _functions = functions;
        _firestore = firestore;
        _auth = auth;
    }

    public async Task<OperationResult<QrPaymentResult>> CreateQrPaymentAsync(
        string listingId, ListingPackage package, CancellationToken ct = default)
    {
        var result = await _functions.CallAsync(
            "createQrPayment",
            new { listingId, package = package.ToString() },
            ct);

        if (!result.Succeeded) return OperationResult<QrPaymentResult>.Fail(result.Error!);

        var payload = result.Value;
        if (payload.ValueKind != JsonValueKind.Object)
            return OperationResult<QrPaymentResult>.Fail("NUTrade sent back something unexpected. Try again.");

        var requiresPayment = !payload.TryGetProperty("requiresPayment", out var rp)
                              || rp.ValueKind != JsonValueKind.False;

        if (!requiresPayment)
        {
            return OperationResult<QrPaymentResult>.Ok(new QrPaymentResult
            {
                RequiresPayment = false,
                AmountCentavos = 0,
            });
        }

        return OperationResult<QrPaymentResult>.Ok(new QrPaymentResult
        {
            RequiresPayment = true,
            PaymentId = ReadString(payload, "paymentId") ?? string.Empty,
            QrImageUrl = ReadString(payload, "qrImageUrl"),
            QrImageBase64 = ReadString(payload, "qrImageBase64"),
            QrPayload = ReadString(payload, "qrPayload"),
            RedirectUrl = ReadString(payload, "redirectUrl"),
            AmountCentavos = payload.TryGetProperty("amountCentavos", out var amount) && amount.TryGetInt64(out var a)
                ? a
                : NUTradeConstants.FeeForPackage(package),
            ExpiresAt = payload.TryGetProperty("expiresAt", out var expires) && expires.TryGetInt64(out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                : DateTimeOffset.UtcNow.AddMinutes(NUTradeConstants.QrExpiryMinutes),
            TestMode = ReadBool(payload, "testMode"),
        });
    }

    public async Task<OperationResult<ListingPaymentCheck>> CheckListingPaymentAsync(string listingId, CancellationToken ct = default)
    {
        var result = await _functions.CallAsync("checkListingPayment", new { listingId }, ct);
        if (!result.Succeeded) return OperationResult<ListingPaymentCheck>.Fail(result.Error!);

        var payload = result.Value;
        if (payload.ValueKind != JsonValueKind.Object)
            return OperationResult<ListingPaymentCheck>.Ok(new ListingPaymentCheck());

        return OperationResult<ListingPaymentCheck>.Ok(new ListingPaymentCheck
        {
            QrImageUrl = ReadString(payload, "qrImageUrl"),
            QrImageBase64 = ReadString(payload, "qrImageBase64"),
            QrPayload = ReadString(payload, "qrPayload"),
            ExpiresAt = payload.TryGetProperty("expiresAt", out var expires) && expires.TryGetInt64(out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                : null,
            Replaced = ReadBool(payload, "replaced"),
            TestMode = ReadBool(payload, "testMode"),
        });
    }

    private static bool ReadBool(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    public async Task<Payment?> GetPaymentForListingAsync(string listingId, CancellationToken ct = default)
    {
        if (_auth.CurrentUid is null || string.IsNullOrWhiteSpace(listingId)) return null;

        var query = Q.Build(
            Q.From("payments"),
            Q.Equal("listingId", Fs.Str(listingId)),
            new[] { Q.OrderBy("createdAt", descending: true) },
            limit: 1);

        var documents = await _firestore.RunQueryAsync(string.Empty, query, ct);
        return documents.Count == 0 ? null : Map(documents[0]);
    }

    /// <summary>Reads a string that the Function may legitimately have returned as JSON null.</summary>
    private static string? ReadString(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static Payment Map(JsonElement document)
    {
        var fields = document.GetProperty("fields");
        return new Payment
        {
            Id = Fs.IdFromName(document),
            ListingId = Fs.StringOr(fields, "listingId"),
            Uid = Fs.StringOr(fields, "uid"),
            Package = Fs.Enum(fields, "package", ListingPackage.Additional),
            Amount = Fs.Long(fields, "amount"),
            PaymongoIntentId = Fs.StringOr(fields, "paymongoIntentId"),
            Status = WireCodec.ToPaymentStatus(Fs.String(fields, "status")),
            QrExpiresAt = Fs.Timestamp(fields, "qrExpiresAt"),
            CreatedAt = Fs.Timestamp(fields, "createdAt") ?? DateTimeOffset.UtcNow,
        };
    }
}

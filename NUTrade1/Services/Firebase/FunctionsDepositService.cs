using System.Text.Json;
using NUTrade1.Core;

namespace NUTrade1.Services.Firebase;

/// <summary>
/// <see cref="IDepositService"/> over the <c>requestBid</c> / <c>checkBidDeposit</c>
/// callables, with the deposit history read straight from <c>bidIntents</c>.
///
/// Both writes are Functions calls because a bid is server-owned: the deposit has to be
/// minted with our PayMongo key, and the bid itself is only written once PayMongo confirms
/// the money. firestore.rules denies the client either.
/// </summary>
public sealed class FunctionsDepositService : IDepositService
{
    private readonly FunctionsClient _functions;
    private readonly FirestoreClient _firestore;
    private readonly IAuthService _auth;

    public FunctionsDepositService(FunctionsClient functions, FirestoreClient firestore, IAuthService auth)
    {
        _functions = functions;
        _firestore = firestore;
        _auth = auth;
    }

    public async Task<OperationResult<BidDeposit>> RequestBidAsync(
        string listingId, long amountCentavos, CancellationToken ct = default)
    {
        var result = await _functions.CallAsync(
            "requestBid", new { listingId, amountCentavos }, ct);
        if (!result.Succeeded) return OperationResult<BidDeposit>.Fail(result.Error!);

        var payload = result.Value;
        var id = ReadString(payload, "bidIntentId");
        if (string.IsNullOrEmpty(id))
            return OperationResult<BidDeposit>.Fail("NUTrade sent back something unexpected. Try again.");

        return OperationResult<BidDeposit>.Ok(new BidDeposit
        {
            Id = id,
            ListingId = listingId,
            AmountCentavos = ReadLong(payload, "amountCentavos"),
            DepositCentavos = ReadLong(payload, "depositCentavos"),
            DepositPercent = (int)ReadLong(payload, "depositPercent"),
            CreditAppliedCentavos = ReadLong(payload, "creditAppliedCentavos"),
            QrDueCentavos = ReadLong(payload, "qrDueCentavos"),
            CoveredByCredit = ReadBool(payload, "coveredByCredit"),
            Status = ReadString(payload, "status") == "locked_in_escrow"
                ? DepositStatus.LockedInEscrow
                : DepositStatus.AwaitingPayment,
            CommittedBidId = ReadString(payload, "bidId"),
            QrImageUrl = ReadString(payload, "qrImageUrl"),
            QrImageBase64 = ReadString(payload, "qrImageBase64"),
            QrPayload = ReadString(payload, "qrPayload"),
            QrExpiresAt = ReadEpochMillis(payload, "expiresAt"),
            TestMode = ReadBool(payload, "testMode"),
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    public async Task<OperationResult<BidDeposit>> CheckDepositAsync(
        string bidIntentId, CancellationToken ct = default)
    {
        var result = await _functions.CallAsync("checkBidDeposit", new { bidIntentId }, ct);
        if (!result.Succeeded) return OperationResult<BidDeposit>.Fail(result.Error!);

        // The callable answers with the state only; the full record is read back so the
        // screen keeps its amounts and QR without the Function having to resend them.
        var document = await _firestore.GetDocumentAsync($"bidIntents/{bidIntentId}", ct);
        if (document is not { } doc)
            return OperationResult<BidDeposit>.Fail("That bid is no longer available.");

        return OperationResult<BidDeposit>.Ok(Map(doc, bidIntentId));
    }

    public async Task<IReadOnlyList<BidDeposit>> GetMyDepositsAsync(
        int limit = 25, CancellationToken ct = default)
    {
        if (_auth.CurrentUid is not { } uid) return [];

        var query = Q.Build(
            Q.From("bidIntents"),
            Q.Equal("bidderUid", Fs.Str(uid)),
            [Q.OrderBy("createdAt", descending: true)],
            limit: limit);

        var documents = await _firestore.RunQueryAsync(string.Empty, query, ct);
        return documents.Select(d => Map(d, Fs.IdFromName(d))).ToArray();
    }

    private static BidDeposit Map(JsonElement document, string id)
    {
        var fields = document.GetProperty("fields");
        return new BidDeposit
        {
            Id = id,
            ListingId = Fs.StringOr(fields, "listingId"),
            ListingTitle = Fs.StringOr(fields, "listingTitle"),
            AmountCentavos = Fs.Long(fields, "amountCentavos"),
            DepositCentavos = Fs.Long(fields, "depositCentavos"),
            DepositPercent = Fs.Int32(fields, "depositPercent"),
            CreditAppliedCentavos = Fs.Long(fields, "creditAppliedCentavos"),
            QrDueCentavos = Fs.Long(fields, "qrDueCentavos") > 0
                ? Fs.Long(fields, "qrDueCentavos")
                : Fs.Long(fields, "depositCentavos"),
            Status = WireCodec.ToDepositStatus(Fs.String(fields, "status")),
            QrImageUrl = Fs.String(fields, "qrImageUrl"),
            QrImageBase64 = Fs.String(fields, "qrImageBase64"),
            QrPayload = Fs.String(fields, "qrPayload"),
            QrExpiresAt = Fs.Timestamp(fields, "qrExpiresAt"),
            QrNote = Fs.String(fields, "qrNote"),
            TestMode = Fs.Bool(fields, "paymongoTestMode"),
            CommittedBidId = Fs.String(fields, "committedBidId"),
            RefundReason = Fs.String(fields, "refundReason"),
            CreatedAt = Fs.Timestamp(fields, "createdAt"),
            ResolvedAt = Fs.Timestamp(fields, "resolvedAt"),
        };
    }

    private static string? ReadString(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBool(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.True;

    private static long ReadLong(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(name, out var value)
        && value.TryGetInt64(out var parsed)
            ? parsed
            : 0;

    private static DateTimeOffset? ReadEpochMillis(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(name, out var value)
        && value.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;
}

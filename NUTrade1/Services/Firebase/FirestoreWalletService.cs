using System.Text.Json;
using NUTrade1.Core;

namespace NUTrade1.Services.Firebase;

/// <summary>
/// <see cref="IWalletService"/> over <c>wallets/{uid}</c>, <c>ledgerEntries</c> and
/// <c>payoutRequests</c>.
///
/// Reads are direct Firestore queries — firestore.rules scopes each of them to the
/// signed-in student. The one write, raising a payout, goes through the
/// <c>requestPayout</c> callable, because it has to debit the balance in the same
/// transaction that records the request and no client may touch a balance.
/// </summary>
public sealed class FirestoreWalletService : IWalletService
{
    private readonly FirestoreClient _firestore;
    private readonly FunctionsClient _functions;
    private readonly IAuthService _auth;

    public FirestoreWalletService(FirestoreClient firestore, FunctionsClient functions, IAuthService auth)
    {
        _firestore = firestore;
        _functions = functions;
        _auth = auth;
    }

    public async Task<Wallet> GetWalletAsync(CancellationToken ct = default)
    {
        if (_auth.CurrentUid is not { } uid) return new Wallet();

        // A student who has never been credited has no wallet document at all, which is
        // a zero balance rather than an error.
        var document = await _firestore.GetDocumentAsync($"wallets/{uid}", ct);
        if (document is not { } doc) return new Wallet { Uid = uid };

        var fields = doc.GetProperty("fields");
        return new Wallet
        {
            Uid = uid,
            BalanceCentavos = Fs.Long(fields, "balanceCentavos"),
            UpdatedAt = Fs.Timestamp(fields, "updatedAt"),
        };
    }

    public async Task<IReadOnlyList<LedgerEntry>> GetLedgerAsync(int limit = 25, CancellationToken ct = default)
    {
        if (_auth.CurrentUid is not { } uid) return [];

        var query = Q.Build(
            Q.From("ledgerEntries"),
            Q.Equal("uid", Fs.Str(uid)),
            [Q.OrderBy("createdAt", descending: true)],
            limit: limit);

        var documents = await _firestore.RunQueryAsync(string.Empty, query, ct);
        return documents.Select(MapEntry).ToArray();
    }

    public async Task<IReadOnlyList<PayoutRequest>> GetMyPayoutsAsync(int limit = 10, CancellationToken ct = default)
    {
        if (_auth.CurrentUid is not { } uid) return [];

        var query = Q.Build(
            Q.From("payoutRequests"),
            Q.Equal("uid", Fs.Str(uid)),
            [Q.OrderBy("createdAt", descending: true)],
            limit: limit);

        var documents = await _firestore.RunQueryAsync(string.Empty, query, ct);
        return documents.Select(MapPayout).ToArray();
    }

    public async Task<OperationResult> RequestPayoutAsync(
        PayoutDestination destination, CancellationToken ct = default)
    {
        var result = await _functions.CallAsync("requestPayout", new
        {
            method = WireCodec.ToWire(destination.Method),
            accountName = destination.AccountName.Trim(),
            accountNumber = destination.AccountNumber.Trim(),
        }, ct);

        return result.Succeeded ? OperationResult.Ok() : OperationResult.Fail(result.Error!);
    }

    internal static LedgerEntry MapEntry(JsonElement document)
    {
        var fields = document.GetProperty("fields");
        return new LedgerEntry
        {
            Id = Fs.IdFromName(document),
            Uid = Fs.StringOr(fields, "uid"),
            Kind = WireCodec.ToLedgerKind(Fs.String(fields, "kind")),
            AmountCentavos = Fs.Long(fields, "amountCentavos"),
            ListingId = Fs.String(fields, "listingId"),
            BidId = Fs.String(fields, "bidId"),
            PayoutRequestId = Fs.String(fields, "payoutRequestId"),
            Note = Fs.String(fields, "note"),
            CreatedAt = Fs.Timestamp(fields, "createdAt"),
        };
    }

    internal static PayoutRequest MapPayout(JsonElement document)
    {
        var fields = document.GetProperty("fields");
        return new PayoutRequest
        {
            Id = Fs.IdFromName(document),
            Uid = Fs.StringOr(fields, "uid"),
            AmountCentavos = Fs.Long(fields, "amountCentavos"),
            Method = WireCodec.ToPayoutMethod(Fs.String(fields, "method")),
            AccountName = Fs.StringOr(fields, "accountName"),
            AccountNumber = Fs.StringOr(fields, "accountNumber"),
            Status = WireCodec.ToPayoutStatus(Fs.String(fields, "status")),
            DeclineReason = Fs.String(fields, "declineReason"),
            CreatedAt = Fs.Timestamp(fields, "createdAt"),
            ResolvedAt = Fs.Timestamp(fields, "resolvedAt"),
        };
    }
}

/// <summary>
/// <see cref="IPayoutModerationService"/> over the admin payout callables. The queue read
/// is a plain query — firestore.rules lets an admin read every request — and both
/// decisions go through Functions, which re-check the admin claim.
/// </summary>
public sealed class FunctionsPayoutModerationService : IPayoutModerationService
{
    private readonly FirestoreClient _firestore;
    private readonly FunctionsClient _functions;

    public FunctionsPayoutModerationService(FirestoreClient firestore, FunctionsClient functions)
    {
        _firestore = firestore;
        _functions = functions;
    }

    public async Task<IReadOnlyList<PayoutRequest>> GetPendingPayoutsAsync(CancellationToken ct = default)
    {
        // Oldest first: a payout queue worked newest-first is one where somebody waits
        // forever.
        var query = Q.Build(
            Q.From("payoutRequests"),
            Q.Equal("status", Fs.Str("requested")),
            [Q.OrderBy("createdAt", descending: false)],
            limit: 50);

        var documents = await _firestore.RunQueryAsync(string.Empty, query, ct);
        return documents.Select(FirestoreWalletService.MapPayout).ToArray();
    }

    public async Task<OperationResult> MarkPaidAsync(string payoutRequestId, CancellationToken ct = default)
    {
        var result = await _functions.CallAsync("markPayoutPaid", new { payoutRequestId }, ct);
        return result.Succeeded ? OperationResult.Ok() : OperationResult.Fail(result.Error!);
    }

    public async Task<OperationResult> DeclineAsync(
        string payoutRequestId, string? reason, CancellationToken ct = default)
    {
        var result = await _functions.CallAsync("declinePayout", new { payoutRequestId, reason }, ct);
        return result.Succeeded ? OperationResult.Ok() : OperationResult.Fail(result.Error!);
    }
}

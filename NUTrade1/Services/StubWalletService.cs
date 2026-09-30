using NUTrade1.Core;

namespace NUTrade1.Services;

/// <summary>
/// Offline stand-in for <see cref="IWalletService"/>. Seeded with a balance and a couple
/// of ledger rows so the wallet card, the ledger list and the payout form can all be
/// clicked through with no network; a payout moves the balance in memory and nowhere else.
/// </summary>
public sealed class StubWalletService : IWalletService, IPayoutModerationService
{
    private const long MinPayoutCentavos = 10_000;

    private long _balanceCentavos = 24_500;
    private readonly List<LedgerEntry> _ledger;
    private readonly List<PayoutRequest> _payouts = [];

    public StubWalletService()
    {
        var now = DateTimeOffset.UtcNow;
        _ledger =
        [
            new LedgerEntry
            {
                Id = "offline-1", Kind = LedgerKind.DepositCredit, AmountCentavos = 15_000,
                Note = "Handover confirmed", CreatedAt = now.AddDays(-1),
            },
            new LedgerEntry
            {
                Id = "offline-2", Kind = LedgerKind.RefundCredit, AmountCentavos = 9_500,
                Note = "Seller cancelled", CreatedAt = now.AddDays(-4),
            },
        ];
    }

    public Task<Wallet> GetWalletAsync(CancellationToken ct = default) =>
        Task.FromResult(new Wallet
        {
            Uid = "offline",
            BalanceCentavos = _balanceCentavos,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

    public Task<IReadOnlyList<LedgerEntry>> GetLedgerAsync(int limit = 25, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LedgerEntry>>(
            _ledger.OrderByDescending(e => e.CreatedAt).Take(limit).ToArray());

    public Task<IReadOnlyList<PayoutRequest>> GetMyPayoutsAsync(int limit = 10, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PayoutRequest>>(
            _payouts.OrderByDescending(p => p.CreatedAt).Take(limit).ToArray());

    public Task<OperationResult> RequestPayoutAsync(
        PayoutDestination destination, CancellationToken ct = default)
    {
        if (_balanceCentavos < MinPayoutCentavos)
        {
            return Task.FromResult(OperationResult.Fail(
                $"You need at least {Money.ToDisplay(MinPayoutCentavos)} before you can cash out."));
        }

        var amount = _balanceCentavos;
        var now = DateTimeOffset.UtcNow;

        // Debited on request, as online — the offline build should show the same states.
        _balanceCentavos = 0;
        _payouts.Add(new PayoutRequest
        {
            Id = $"offline-payout-{_payouts.Count + 1}",
            AmountCentavos = amount,
            Method = destination.Method,
            AccountName = destination.AccountName,
            AccountNumber = destination.AccountNumber,
            Status = PayoutStatus.Requested,
            CreatedAt = now,
        });
        _ledger.Add(new LedgerEntry
        {
            Id = $"offline-ledger-{_ledger.Count + 1}",
            Kind = LedgerKind.Payout,
            AmountCentavos = -amount,
            Note = $"Payout to {destination.Method}",
            CreatedAt = now,
        });

        return Task.FromResult(OperationResult.Ok());
    }

    public Task<IReadOnlyList<PayoutRequest>> GetPendingPayoutsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PayoutRequest>>(
            _payouts.Where(p => p.IsPending).OrderBy(p => p.CreatedAt).ToArray());

    public Task<OperationResult> MarkPaidAsync(string payoutRequestId, CancellationToken ct = default) =>
        Task.FromResult(Resolve(payoutRequestId, PayoutStatus.Paid, null));

    public Task<OperationResult> DeclineAsync(
        string payoutRequestId, string? reason, CancellationToken ct = default) =>
        Task.FromResult(Resolve(payoutRequestId, PayoutStatus.Declined, reason));

    private OperationResult Resolve(string id, PayoutStatus status, string? reason)
    {
        if (_payouts.FirstOrDefault(p => p.Id == id) is not { IsPending: true } payout)
            return OperationResult.Fail("That request has already been resolved.");

        payout.Status = status;
        payout.DeclineReason = reason;
        payout.ResolvedAt = DateTimeOffset.UtcNow;

        if (status == PayoutStatus.Declined)
        {
            _balanceCentavos += payout.AmountCentavos;
            _ledger.Add(new LedgerEntry
            {
                Id = $"offline-ledger-{_ledger.Count + 1}",
                Kind = LedgerKind.RefundCredit,
                AmountCentavos = payout.AmountCentavos,
                Note = reason is { Length: > 0 } r ? $"Payout declined: {r}" : "Payout declined",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        return OperationResult.Ok();
    }
}

using NUTrade1.Core;

namespace NUTrade1.Services;

/// <summary>
/// Offline stand-in for <see cref="IDepositService"/>. Mints a fake deposit with no QR and
/// commits it on the second check, so the payment screen's "waiting, then paid" sequence
/// can be clicked through with no network.
/// </summary>
public sealed class StubDepositService : IDepositService
{
    private const int DepositPercent = 15;

    private readonly List<BidDeposit> _deposits = [];
    private readonly HashSet<string> _checkedOnce = [];

    public Task<OperationResult<BidDeposit>> RequestBidAsync(
        string listingId, long amountCentavos, CancellationToken ct = default)
    {
        var deposit = new BidDeposit
        {
            Id = $"offline-deposit-{_deposits.Count + 1}",
            ListingId = listingId,
            ListingTitle = "Offline listing",
            AmountCentavos = amountCentavos,
            DepositCentavos = RoundUpToPeso(amountCentavos * DepositPercent / 100),
            DepositPercent = DepositPercent,
            Status = DepositStatus.AwaitingPayment,
            QrExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _deposits.Add(deposit);
        return Task.FromResult(OperationResult<BidDeposit>.Ok(deposit));
    }

    public Task<OperationResult<BidDeposit>> CheckDepositAsync(
        string bidIntentId, CancellationToken ct = default)
    {
        if (_deposits.FirstOrDefault(d => d.Id == bidIntentId) is not { } deposit)
            return Task.FromResult(OperationResult<BidDeposit>.Fail("That bid is no longer available."));

        // First check says "not yet", the second commits — enough to exercise both states.
        if (deposit.IsAwaitingPayment && !_checkedOnce.Add(bidIntentId))
        {
            deposit.Status = DepositStatus.LockedInEscrow;
            deposit.CommittedBidId = $"offline-bid-{bidIntentId}";
        }

        return Task.FromResult(OperationResult<BidDeposit>.Ok(deposit));
    }

    public Task<IReadOnlyList<BidDeposit>> GetMyDepositsAsync(int limit = 25, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BidDeposit>>(
            _deposits.OrderByDescending(d => d.CreatedAt).Take(limit).ToArray());

    /// <summary>Mirrors depositFor() — students are never asked to scan for stray centavos.</summary>
    private static long RoundUpToPeso(long centavos) => (long)Math.Ceiling(centavos / 100d) * 100;
}

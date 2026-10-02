using NUTrade1.Core;

namespace NUTrade1.Services;

/// <summary>
/// Offline stand-in for <see cref="IPaymentService"/>. Reports every package as
/// settled-for-free, since there is no Function to broker a real QR code, and hands
/// the listing to the admin queue exactly as <c>createQrPayment</c> would.
/// </summary>
public sealed class StubPaymentService : IPaymentService
{
    private readonly IListingService _listings;

    public StubPaymentService(IListingService listings) => _listings = listings;

    public Task<OperationResult<QrPaymentResult>> CreateQrPaymentAsync(
        string listingId,
        ListingPackage package,
        CancellationToken ct = default)
    {
        (_listings as StubListingService)?.SubmitForApproval(listingId, package);
        return Task.FromResult(OperationResult<QrPaymentResult>.Ok(new QrPaymentResult
        {
            RequiresPayment = false,
            AmountCentavos = 0,
        }));
    }

    public Task<Payment?> GetPaymentForListingAsync(string listingId, CancellationToken ct = default) =>
        Task.FromResult<Payment?>(null);

    /// <summary>Nothing to check offline: every stub post is settled for free on the spot.</summary>
    public Task<OperationResult<ListingPaymentCheck>> CheckListingPaymentAsync(string listingId, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<ListingPaymentCheck>.Ok(new ListingPaymentCheck()));
}

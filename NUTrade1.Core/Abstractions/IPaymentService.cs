namespace NUTrade1.Core;

/// <summary>
/// Brokers the listing fee. The app calls a Cloud Function to mint a QR Ph code and
/// then watches the listing document; it never talks to PayMongo directly.
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Invokes the <c>createQrPayment</c> callable for a listing already in
    /// <see cref="ListingStatus.PendingPayment"/>, returning a QR to display.
    /// </summary>
    Task<OperationResult<QrPaymentResult>> CreateQrPaymentAsync(
        string listingId,
        ListingPackage package,
        CancellationToken ct = default);

    /// <summary>Reads the current payment doc for a listing, if one exists.</summary>
    Task<Payment?> GetPaymentForListingAsync(string listingId, CancellationToken ct = default);

    /// <summary>
    /// Asks the backend to check with PayMongo whether this listing's fee has been paid,
    /// and to settle it if so (<c>checkListingPayment</c>). Covers a late or missing
    /// webhook; the listing document reflects the outcome either way.
    /// </summary>
    Task<OperationResult> CheckListingPaymentAsync(string listingId, CancellationToken ct = default);
}

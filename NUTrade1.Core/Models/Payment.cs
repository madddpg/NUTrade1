namespace NUTrade1.Core;

/// <summary>
/// Maps to <c>payments/{paymentId}</c>. Written only by Cloud Functions; the client
/// may read its own payment doc to drive the QR screen.
/// </summary>
public sealed class Payment
{
    public string Id { get; set; } = string.Empty;

    public string ListingId { get; set; } = string.Empty;

    /// <summary>UID of the paying listing owner.</summary>
    public string Uid { get; set; } = string.Empty;

    public ListingPackage Package { get; set; } = ListingPackage.Additional;

    /// <summary>Amount in centavos.</summary>
    public long Amount { get; set; }

    /// <summary>PayMongo Payment Intent id.</summary>
    public string PaymongoIntentId { get; set; } = string.Empty;

    public PaymentStatus Status { get; set; } = PaymentStatus.AwaitingPayment;

    public DateTimeOffset? QrExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

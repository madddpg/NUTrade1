namespace NUTrade1.Core;

/// <summary>
/// What the <c>createQrPayment</c> callable Function returns to the payment screen.
/// </summary>
public sealed class QrPaymentResult
{
    /// <summary>
    /// False when the auction published straight away with no money involved — a
    /// verified student's first concurrent auction is free, and the Function flips it
    /// to <see cref="ListingStatus.Active"/> without ever calling PayMongo. The payment
    /// screen shows a confirmation instead of a QR code.
    /// </summary>
    public bool RequiresPayment { get; set; } = true;

    public string PaymentId { get; set; } = string.Empty;

    /// <summary>A hosted PNG of the QR Ph code.</summary>
    public string? QrImageUrl { get; set; }

    /// <summary>An inline PNG (base64, no data-URI prefix) of the QR Ph code.</summary>
    public string? QrImageBase64 { get; set; }

    /// <summary>Raw EMVCo payload, to be rendered locally when no image is supplied.</summary>
    public string? QrPayload { get; set; }

    /// <summary>Hosted checkout page, when PayMongo returns a redirect rather than a code.</summary>
    public string? RedirectUrl { get; set; }

    public long AmountCentavos { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// The PayMongo secret in use is a test key. A real GCash or Maya app rejects the
    /// code and says payment failed.
    /// </summary>
    public bool TestMode { get; set; }
}

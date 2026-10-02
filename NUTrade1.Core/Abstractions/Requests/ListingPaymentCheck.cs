namespace NUTrade1.Core;

/// <summary>
/// What <c>checkListingPayment</c> says about the fee QR on screen.
/// A failed scan uses up that code; <see cref="Replaced"/> means a new one is ready.
/// </summary>
public sealed class ListingPaymentCheck
{
    public string? QrImageUrl { get; init; }
    public string? QrImageBase64 { get; init; }
    public string? QrPayload { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool Replaced { get; init; }
    public bool TestMode { get; init; }
}

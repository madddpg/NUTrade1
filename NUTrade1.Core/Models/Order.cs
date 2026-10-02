namespace NUTrade1.Core;

/// <summary>
/// Maps to <c>orders/{orderId}</c> — what the winning bidder owes the seller once a
/// bid is approved. Created by a Cloud Function alongside the chat room; read-only to
/// both participants.
/// </summary>
public sealed class Order
{
    public string Id { get; set; } = string.Empty;

    public string ListingId { get; set; } = string.Empty;

    public string ListingTitle { get; set; } = string.Empty;

    /// <summary>First listing photo, denormalized so the payment screen needs one read.</summary>
    public string? ListingPhoto { get; set; }

    public string BidId { get; set; } = string.Empty;

    public string ChatId { get; set; } = string.Empty;

    public string SellerUid { get; set; } = string.Empty;

    public string BuyerUid { get; set; } = string.Empty;

    public string BuyerName { get; set; } = string.Empty;

    /// <summary>The winning bid, in centavos.</summary>
    public long AmountCentavos { get; set; }

    /// <summary>Short code identifying the order, shown on the payment screen.</summary>
    public string Reference { get; set; } = string.Empty;

    public OrderStatus Status { get; set; } = OrderStatus.AwaitingPayment;

    /// <summary>Pay by this moment or the bid is released and the auction reopens.</summary>
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>Hosted PNG of the QR Ph code, once one has been minted.</summary>
    public string? QrImageUrl { get; set; }

    /// <summary>Inline PNG (base64, no data-URI prefix) of the QR Ph code.</summary>
    public string? QrImageBase64 { get; set; }

    /// <summary>Raw EMVCo payload, when PayMongo returns no image.</summary>
    public string? QrPayload { get; set; }

    /// <summary>
    /// When the current QR lapses — roughly ten minutes. Much shorter than
    /// <see cref="DueAt"/>, so the payment screen mints a fresh code on demand
    /// for as long as the order stands.
    /// </summary>
    public DateTimeOffset? QrExpiresAt { get; set; }

    /// <summary>The code was minted with a PayMongo test key. A real wallet will reject it.</summary>
    public bool TestMode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public bool IsSettled => Status == OrderStatus.Paid;

    public bool IsOpen => Status == OrderStatus.AwaitingPayment;

    /// <summary>True when the minted QR has lapsed and a new one is needed.</summary>
    public bool NeedsFreshQr =>
        QrExpiresAt is null || QrExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30);

    public bool IsBuyer(string? uid) => uid is not null && uid == BuyerUid;

    public bool IsSeller(string? uid) => uid is not null && uid == SellerUid;
}

namespace NUTrade1.Core;

/// <summary>Maps to <c>transactions/{txId}</c>. Written by a Function when a trade is marked completed.</summary>
public sealed class TradeTransaction
{
    public string Id { get; set; } = string.Empty;

    public string ListingId { get; set; } = string.Empty;

    public string BuyerUid { get; set; } = string.Empty;

    public string SellerUid { get; set; } = string.Empty;

    public DateTimeOffset CompletedAt { get; set; }
}

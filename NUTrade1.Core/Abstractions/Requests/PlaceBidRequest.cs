namespace NUTrade1.Core;

/// <summary>Payload for placing a bid against an active auction listing.</summary>
public sealed class PlaceBidRequest
{
    public string ListingId { get; set; } = string.Empty;

    public long AmountCentavos { get; set; }
}

namespace NUTrade1.Core;

/// <summary>Maps to <c>listings/{listingId}/bids/{bidId}</c> in Firestore.</summary>
public sealed class Bid
{
    public string Id { get; set; } = string.Empty;

    public string ListingId { get; set; } = string.Empty;

    /// <summary>UID of the student who placed the bid.</summary>
    public string BidderUid { get; set; } = string.Empty;

    /// <summary>Display name of the bidder, denormalized for the bid history / incoming-bids list.</summary>
    public string BidderName { get; set; } = string.Empty;

    public long AmountCentavos { get; set; }

    public BidStatus Status { get; set; } = BidStatus.Pending;

    /// <summary>Set once the bid is approved and a chat room is created.</summary>
    public string? ChatId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

using NUTrade1.Core;

namespace NUTrade1.ViewModels;

/// <summary>Display wrapper joining a <see cref="Bid"/> the current user placed with its listing,
/// for the "My Bids" tab.</summary>
public sealed class MyBidViewModel
{
    public MyBidViewModel(Bid bid, Listing listing)
    {
        Bid = bid;
        Listing = listing;
    }

    public Bid Bid { get; }
    public Listing Listing { get; }

    public string Title => Listing.Title;
    public string? CoverPhoto => Listing.Photos.FirstOrDefault();
    public string YourBidDisplay => Money.ToDisplay(Bid.AmountCentavos);
    public string CurrentBidDisplay => Money.ToDisplay(Listing.CurrentHighestBidCentavos);
    public DateTimeOffset CreatedAt => Bid.CreatedAt;

    public string StatusText => Bid.Status switch
    {
        BidStatus.Pending => "Winning",
        BidStatus.Outbid => "Outbid",
        BidStatus.Approved => "Won",
        BidStatus.Declined => "Declined",
        BidStatus.Withdrawn => "Withdrawn",
        _ => "Pending",
    };

    /// <summary>Winning opens a chat. The bid stores the room id awardListing wrote.</summary>
    public bool CanMessageSeller =>
        Bid.Status == BidStatus.Approved && !string.IsNullOrEmpty(Bid.ChatId);
}

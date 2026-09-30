using NUTrade1.Core;

namespace NUTrade1.ViewModels;

/// <summary>Display wrapper joining a pending <see cref="Bid"/> on one of the current user's
/// listings with that listing's title, for the Profile "Incoming bids" section.</summary>
public sealed class IncomingBidViewModel
{
    public IncomingBidViewModel(Bid bid, Listing listing)
    {
        Bid = bid;
        Listing = listing;
    }

    public Bid Bid { get; }
    public Listing Listing { get; }

    public string BidId => Bid.Id;
    public string BidderName => Bid.BidderName;
    public string ListingTitle => Listing.Title;
    public string AmountDisplay => Money.ToDisplay(Bid.AmountCentavos);
    public DateTimeOffset CreatedAt => Bid.CreatedAt;
}

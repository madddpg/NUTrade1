using System.ComponentModel;

namespace NUTrade1.Core;

/// <summary>Maps to <c>listings/{listingId}</c> in Firestore.</summary>
public sealed class Listing : INotifyPropertyChanged
{
    public string Id { get; set; } = string.Empty;

    public string OwnerUid { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public ItemCondition Condition { get; set; } = ItemCondition.Unknown;

    public ItemCategory Category { get; set; } = ItemCategory.Unknown;

    /// <summary>What the seller typed when <see cref="Category"/> is <c>Other</c>; empty otherwise.</summary>
    public string CategoryOther { get; set; } = string.Empty;

    /// <summary>
    /// The category to show a buyer: the seller's own words when they picked "Other", the
    /// humanized enum name otherwise. Listings written before "Other" required a value fall
    /// back to "Other".
    /// </summary>
    public string CategoryLabel => Category == ItemCategory.Other && CategoryOther.Length > 0
        ? CategoryOther
        : Humanize(Category);

    /// <summary>1–4 Storage paths for the item's photos.</summary>
    public IList<string> Photos { get; set; } = new List<string>();

    public CampusZone CampusZone { get; set; } = CampusZone.Unknown;

    /// <summary>What the seller typed when <see cref="CampusZone"/> is <c>Other</c>; empty otherwise.</summary>
    public string CampusZoneOther { get; set; } = string.Empty;

    /// <summary>The meetup location to show a buyer. Empty when the listing predates the fixed list.</summary>
    public string MeetupLabel => CampusZone switch
    {
        CampusZone.Unknown => string.Empty,
        CampusZone.Other => CampusZoneOther.Length > 0 ? CampusZoneOther : "Elsewhere on campus",
        CampusZone.Itso => "ITSO",
        CampusZone.Sdao => "SDAO",
        CampusZone.Avr => "AVR",
        CampusZone.AccountingAndRegistrarOffice => "Accounting and Registrar Office",
        var zone => Humanize(zone),
    };

    public ListingPackage Package { get; set; } = ListingPackage.Free;

    /// <summary>
    /// Auction, set price, or swap. Missing on older documents, which are auctions.
    /// </summary>
    public ListingKind Kind { get; set; } = ListingKind.Auction;

    /// <summary>True for a timed auction. Bids, the bid history, and the auction clock are for these only.</summary>
    public bool IsAuction => Kind == ListingKind.Auction;

    /// <summary>Short label for a feed badge: Auction, Buy, or Swap.</summary>
    public string KindLabel => Kind switch
    {
        ListingKind.Standard => "Buy",
        ListingKind.Swap => "Swap",
        _ => "Auction",
    };

    /// <summary>Caption above the figure on a card.</summary>
    public string OfferCaption => Kind switch
    {
        ListingKind.Standard => "PRICE",
        ListingKind.Swap => "SWAP",
        _ => "HIGHEST BID",
    };

    /// <summary>The figure under <see cref="OfferCaption"/>.</summary>
    public string OfferDisplay => Kind switch
    {
        ListingKind.Standard => Money.ToDisplay(StartingBidCentavos),
        ListingKind.Swap => "Open",
        _ => Money.ToDisplay(CurrentHighestBidCentavos),
    };

    /// <summary>The line under the price on a feed card.</summary>
    public string FeedFootnote => Kind == ListingKind.Auction
        ? $"{BidCount} bids · refreshes hourly"
        : "Refreshes hourly";

    /// <summary>Shown to a buyer on a listing that does not take bids. Empty for auctions.</summary>
    public string BuyerNote => Kind switch
    {
        ListingKind.Standard => "This is a set price. Meet the seller on campus to buy it — it is not an auction.",
        ListingKind.Swap => "The seller wants to swap this item. Arrange the trade with them on campus.",
        _ => string.Empty,
    };

    /// <summary>Set true by a Function when a <see cref="ListingPackage.Priority"/> fee is paid.</summary>
    public bool IsPinned { get; set; }

    public ListingStatus Status { get; set; } = ListingStatus.Draft;

    /// <summary>Opening bid, in centavos, before any bids are placed.</summary>
    public long StartingBidCentavos { get; set; }

    /// <summary>Minimum amount, in centavos, a new bid must exceed the current highest bid by.</summary>
    public long MinIncrementCentavos { get; set; }

    /// <summary>Optional reserve, in centavos; below this the seller isn't obligated to sell.</summary>
    public long? ReservePriceCentavos { get; set; }

    /// <summary>
    /// True once an admin has approved the listing. Set server-side; the client
    /// cannot write it. Priority does not change this — it only pins the listing.
    /// </summary>
    public bool IsVisible { get; set; }

    /// <summary>When the listing was put on the feed. Older regular posts stored the next hour here while they waited.</summary>
    public DateTimeOffset? VisibleFrom { get; set; }

    /// <summary>Current highest bid, in centavos. Equal to <see cref="StartingBidCentavos"/> until the first bid.</summary>
    public long CurrentHighestBidCentavos { get; set; }

    /// <summary>UID of the current highest bidder, if any.</summary>
    public string? HighestBidderUid { get; set; }

    public int BidCount { get; set; }

    /// <summary>Set by a Function when the auction goes live; drives the countdown.</summary>
    public DateTimeOffset? AuctionEndsAt { get; set; }

    public string? WinningBidId { get; set; }

    /// <summary>The order the winning bid created, once the auction is matched.</summary>
    public string? OrderId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set by <c>approveListing</c> when an admin puts the auction live.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>
    /// The package whose fee actually cleared, stamped server-side when the listing
    /// enters the admin queue. Approval honours this, not the draft's <see cref="Package"/>.
    /// </summary>
    public ListingPackage? PaidPackage { get; set; }

    /// <summary>When the fee cleared and the listing joined the admin queue.</summary>
    public DateTimeOffset? SubmittedForApprovalAt { get; set; }

    /// <summary>The admin's note when <see cref="Status"/> is <see cref="ListingStatus.Rejected"/>.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>Minimum a new bid must reach right now to be valid.</summary>
    public long MinNextBidCentavos =>
        BidCount == 0 ? StartingBidCentavos : CurrentHighestBidCentavos + MinIncrementCentavos;

    /// <summary>
    /// Live auction clock (<c>HH:MM:SS</c> or <c>MM:SS</c>), refreshed by <see cref="TickCountdown"/>.
    /// <c>00:00</c> once <see cref="AuctionEndsAt"/> has passed. Empty when the auction has no end.
    /// </summary>
    public string CountdownText { get; private set; } = string.Empty;

    /// <summary>
    /// True while an auction has an end time, including after it hits <c>00:00</c>.
    /// Standard and swap listings never show this clock.
    /// </summary>
    public bool HasAuctionClock => Kind == ListingKind.Auction && AuctionEndsAt is not null;

    /// <summary>Clock until a hidden listing joins the feed. Empty once it is visible.</summary>
    public string RefreshCountdownText { get; private set; } = string.Empty;

    /// <summary>The hourly-slot clock should be on screen.</summary>
    public bool ShowRefreshCountdown { get; private set; }

    public bool AuctionEnded { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Recomputes the auction clock and the feed-slot clock against <paramref name="now"/>.
    /// Called every second by a page-owned dispatcher timer.</summary>
    public void TickCountdown(DateTimeOffset now)
    {
        var (text, ended) = Kind == ListingKind.Auction
            ? Format(AuctionEndsAt, now)
            : (string.Empty, false);
        var refresh = RefreshClock(now);
        var showRefresh = refresh.Length > 0;
        if (text == CountdownText && ended == AuctionEnded
            && refresh == RefreshCountdownText && showRefresh == ShowRefreshCountdown)
            return;

        CountdownText = text;
        AuctionEnded = ended;
        RefreshCountdownText = refresh;
        ShowRefreshCountdown = showRefresh;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountdownText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AuctionEnded)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RefreshCountdownText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowRefreshCountdown)));
    }

    /// <summary><c>MM:SS</c> / <c>HH:MM:SS</c> while an older listing is still hidden, or empty once it is on the feed.</summary>
    private string RefreshClock(DateTimeOffset now)
    {
        if (IsVisible || VisibleFrom is not { } from) return string.Empty;
        return CountdownClock.Format(from - now);
    }

    /// <summary>`AcademicSupplies` -> `Academic Supplies`. Mirrors EnumDisplayConverter.Humanize,
    /// which lives in the app project and so cannot be reached from here.</summary>
    private static string Humanize<T>(T value) where T : struct, Enum
    {
        var name = value.ToString() ?? string.Empty;
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) sb.Append(' ');
            sb.Append(name[i]);
        }
        return sb.ToString();
    }

    private static (string Text, bool Ended) Format(DateTimeOffset? endsAt, DateTimeOffset now)
    {
        if (endsAt is not { } ends) return (string.Empty, false);

        var remaining = ends - now;
        if (remaining <= TimeSpan.Zero) return ("00:00", true);

        return (CountdownClock.Format(remaining), false);
    }
}

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
    /// False while a regular listing waits for the next hourly refresh to join the
    /// feed. Priority listings are published visible straight away — that is what the
    /// fee buys. Set server-side at publish time; the client cannot write it.
    /// </summary>
    public bool IsVisible { get; set; }

    /// <summary>The hourly slot this listing joins the feed at.</summary>
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

    /// <summary>Live "1h 23m" / "45s" text, refreshed by <see cref="TickCountdown"/>. Empty once ended.</summary>
    public string CountdownText { get; private set; } = string.Empty;

    public bool AuctionEnded { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Recomputes <see cref="CountdownText"/>/<see cref="AuctionEnded"/> against <paramref name="now"/>.
    /// Called every second by a page-owned dispatcher timer while the auction is live.</summary>
    public void TickCountdown(DateTimeOffset now)
    {
        var (text, ended) = Format(AuctionEndsAt, now);
        if (text == CountdownText && ended == AuctionEnded) return;

        CountdownText = text;
        AuctionEnded = ended;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountdownText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AuctionEnded)));
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
        if (remaining <= TimeSpan.Zero) return ("Ended", true);

        return remaining.TotalHours >= 1
            ? ($"{(int)remaining.TotalHours}h {remaining.Minutes}m", false)
            : ($"{remaining.Minutes}m {remaining.Seconds}s", false);
    }
}

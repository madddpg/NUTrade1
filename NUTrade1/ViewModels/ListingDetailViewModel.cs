using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;
using NUTrade1.Services;

namespace NUTrade1.ViewModels;

[QueryProperty(nameof(ListingId), "listingId")]
public partial class ListingDetailViewModel : BaseViewModel
{
    private readonly IListingService _listings;
    private readonly IBidService _bids;
    private readonly IDepositService _deposits;
    private readonly INavigationService _nav;
    private readonly IUserService _users;
    private readonly IAuthService _auth;
    private IDispatcherTimer? _countdownTimer;

    public ListingDetailViewModel(
        IListingService listings,
        IBidService bids,
        IDepositService deposits,
        IUserService users,
        IAuthService auth,
        INavigationService nav)
    {
        _listings = listings;
        _bids = bids;
        _deposits = deposits;
        _nav = nav;
        _users = users;
        _auth = auth;
        Title = "Auction";
    }

    [ObservableProperty] private string? _listingId;
    [ObservableProperty] private Listing? _listing;
    [ObservableProperty] private UserProfile? _seller;
    [ObservableProperty] private string _bidAmountText = string.Empty;
    [ObservableProperty] private bool _bidPlaced;

    public ObservableCollection<Bid> BidHistory { get; } = new();

    public bool IsOwnListing => BidEligibility.IsSeller(Listing, _auth.CurrentUid);

    /// <summary>
    /// The bid form is for buyers only. It stays hidden until the listing has loaded, so a
    /// seller never sees it flash up on their own item while the page is fetching.
    /// </summary>
    public bool ShowBidForm => Listing is not null && !IsOwnListing;

    /// <summary>Why the current student can't bid right now, or null if they can. Mirrors `requestBid`.</summary>
    private string? WhyCantBid() =>
        BidEligibility.WhyNot(Listing, _auth.CurrentUid, _auth.IsVerified, DateTimeOffset.UtcNow);

    public string CurrentBidDisplay => Money.ToDisplay(Listing?.CurrentHighestBidCentavos ?? 0);

    public string MinNextBidDisplay => Money.ToDisplay(Listing?.MinNextBidCentavos ?? 0);

    public string BidCountDisplay => (Listing?.BidCount ?? 0).ToString();

    public override async Task OnAppearingAsync()
    {
        if (string.IsNullOrEmpty(ListingId) || IsBusy) return;
        IsBusy = true;

        // The auction in bones until this listing has loaded. Coming back from the photo
        // viewer or the deposit screen, the listing on screen stays while it refreshes.
        IsLoading = Listing?.Id != ListingId;
        try
        {
            // The bid history doesn't depend on the listing, so it is asked for alongside it.
            var bidsTask = _bids.GetBidsForListingAsync(ListingId);

            Listing = await _listings.GetListingAsync(ListingId);
            Title = Listing?.Title ?? "Auction";
            Seller = Listing is null ? null : await _users.GetProfileAsync(Listing.OwnerUid);
            RaiseListingDependentProps();

            var bids = await bidsTask;
            BidHistory.Clear();
            foreach (var bid in bids)
                BidHistory.Add(bid);

            StartCountdownTimer();
        }
        finally
        {
            IsBusy = false;
            IsLoading = false;
        }
    }

    public override Task OnDisappearingAsync()
    {
        StopCountdownTimer();
        return Task.CompletedTask;
    }

    /// <summary>
    /// What bidding actually costs, shown on the form. Mirrors BID_DEPOSIT_PERCENT — the
    /// exact peso amount depends on the bid, so this states the rule rather than a figure.
    /// </summary>
    public string DepositHint =>
        $"Bidding holds a {NUTradeConstants.BidDepositPercent}% deposit by QR Ph. " +
        "You get it back if you're outbid or don't win.";

    [RelayCommand]
    private async Task PlaceBidAsync()
    {
        ErrorMessage = null;

        if (Listing is null || ListingId is null) return;

        // Checked before anything else, and before any money is asked for. The form is
        // hidden from the seller anyway; this is the guard for every other way in.
        if (WhyCantBid() is { } reason)
        {
            ErrorMessage = reason;
            return;
        }

        if (!long.TryParse(BidAmountText.Trim(), out var pesos) || pesos <= 0)
        {
            ErrorMessage = "Enter a valid bid amount.";
            return;
        }

        var amountCentavos = Money.FromPesos(pesos);
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            // No bid is written here. requestBid validates the amount and mints a QR for
            // the commitment deposit; the bid appears on the listing only once that
            // deposit is confirmed paid, which the next screen waits for.
            var result = await _deposits.RequestBidAsync(ListingId, amountCentavos);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Error ?? "Could not start your bid.";
                return;
            }

            BidAmountText = string.Empty;
            await _nav.GoToAsync(
                Routes.BidDeposit,
                new Dictionary<string, object> { ["bidIntentId"] = result.Value!.Id });
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Opens the full-screen viewer at the photo that was tapped.</summary>
    [RelayCommand]
    private Task OpenPhotoAsync(int index)
    {
        if (Listing is not { Photos.Count: > 0 } listing) return Task.CompletedTask;
        return _nav.GoToAsync(Routes.PhotoViewer, new Dictionary<string, object>
        {
            [PhotoViewerViewModel.PhotosKey] = listing.Photos.ToList(),
            [PhotoViewerViewModel.IndexKey] = index,
            [PhotoViewerViewModel.TitleKey] = listing.Title,
        });
    }

    private void RaiseListingDependentProps()
    {
        OnPropertyChanged(nameof(IsOwnListing));
        OnPropertyChanged(nameof(ShowBidForm));
        OnPropertyChanged(nameof(CurrentBidDisplay));
        OnPropertyChanged(nameof(MinNextBidDisplay));
        OnPropertyChanged(nameof(BidCountDisplay));
    }

    private void StartCountdownTimer()
    {
        if (_countdownTimer is not null || Listing is null) return;
        _countdownTimer = Application.Current?.Dispatcher.CreateTimer();
        if (_countdownTimer is null) return;
        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.Tick += (_, _) => Listing?.TickCountdown(DateTimeOffset.UtcNow);
        _countdownTimer.Start();
        Listing.TickCountdown(DateTimeOffset.UtcNow);
    }

    private void StopCountdownTimer()
    {
        _countdownTimer?.Stop();
        _countdownTimer = null;
    }
}

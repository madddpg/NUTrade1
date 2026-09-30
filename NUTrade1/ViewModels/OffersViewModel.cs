using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;
using NUTrade1.Services;

namespace NUTrade1.ViewModels;

/// <summary>Backs the "My Bids" tab: auctions the signed-in student has bid on.</summary>
public partial class OffersViewModel : BaseViewModel
{
    private readonly IBidService _bids;
    private readonly IListingService _listings;
    private readonly INavigationService _nav;

    public OffersViewModel(IBidService bids, IListingService listings, INavigationService nav)
    {
        _bids = bids;
        _listings = listings;
        _nav = nav;
        Title = "My Bids";
    }

    public ObservableRangeCollection<MyBidViewModel> MyBids { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBids))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private int _bidCount;

    public bool HasBids => BidCount > 0;

    /// <summary>"You haven't placed any bids" — never while the skeleton is standing in for the list.</summary>
    public bool ShowEmptyState => !HasBids && !IsLoading;

    protected override void OnIsLoadingChangedCore(bool value) => OnPropertyChanged(nameof(ShowEmptyState));

    public override async Task OnAppearingAsync()
    {
        if (IsBusy) return;
        IsBusy = true;

        // The skeleton only on a first visit. Coming back to the tab, the bids already on
        // screen stay there while the fresh list loads, then swap in at once.
        IsLoading = MyBids.Count == 0;
        try
        {
            var bids = await _bids.GetMyBidsAsync();

            // Each bid needs its listing; asked for together rather than one after another.
            var listings = await Task.WhenAll(bids.Select(bid => _listings.GetListingAsync(bid.ListingId)));
            MyBids.ReplaceAll(bids.Zip(listings)
                .Where(pair => pair.Second is not null)
                .Select(pair => new MyBidViewModel(pair.First, pair.Second!)));
            BidCount = MyBids.Count;
        }
        finally
        {
            IsBusy = false;
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task OpenListingAsync(MyBidViewModel? item) =>
        item is null
            ? Task.CompletedTask
            : _nav.GoToAsync(Routes.ListingDetail, new Dictionary<string, object> { ["listingId"] = item.Listing.Id });
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;
using NUTrade1.Services;

namespace NUTrade1.ViewModels;

public partial class FeedViewModel : BaseViewModel
{
    private readonly IListingService _listings;
    private readonly INavigationService _nav;
    private string? _cursor;
    private IDispatcherTimer? _countdownTimer;

    /// <summary>
    /// Everything fetched for the current category, before <see cref="SearchText"/> is
    /// applied. Search is a client-side Contains over these, so typing costs nothing —
    /// it used to re-run the Firestore query on every keystroke and throw most of the
    /// results away, which is what made the search box feel like it was lagging.
    /// </summary>
    private readonly List<Listing> _fetched = new();

    /// <summary>
    /// How long a loaded feed is reused when the tab is reopened. OnAppearing fires on
    /// every Shell tab switch, so refetching there unconditionally meant Home → Profile →
    /// Home cost two full Firestore queries and showed an empty list in between. Regular
    /// listings only join the feed on the hour anyway (publishScheduledListings), so a
    /// minute of staleness hides nothing; pull-to-refresh and the filter button still
    /// force a read.
    /// </summary>
    private static readonly TimeSpan FeedFreshFor = TimeSpan.FromSeconds(60);

    private DateTimeOffset _loadedAt;

    public FeedViewModel(IListingService listings, INavigationService nav)
    {
        _listings = listings;
        _nav = nav;
        Title = "Home";
    }

    public ObservableRangeCollection<Listing> Listings { get; } = new();

    [ObservableProperty] private ItemCategory? _selectedCategory;
    [ObservableProperty] private string _searchText = string.Empty;

    public bool HasListings => Listings.Count > 0;

    /// <summary>"No auctions yet" — never while the skeleton is standing in for the feed.</summary>
    public bool ShowEmptyState => !HasListings && !IsLoading;

    /// <summary>The next page is on its way: a row of skeleton cards under the last one.</summary>
    [ObservableProperty] private bool _isLoadingMore;

    public IReadOnlyList<ItemCategory> Categories { get; } =
        Enum.GetValues<ItemCategory>().Where(c => c != ItemCategory.Unknown).ToArray();

    public override async Task OnAppearingAsync()
    {
        // Nothing on screen yet: the skeleton stands in for the feed. A stale feed that is
        // already showing is refreshed quietly behind the cards the student is looking at.
        if (Listings.Count == 0)
            await LoadFeedAsync(showSkeleton: true);
        else if (DateTimeOffset.UtcNow - _loadedAt >= FeedFreshFor)
            await LoadFeedAsync(showSkeleton: false);

        StartCountdownTimer();
    }

    public override Task OnDisappearingAsync()
    {
        StopCountdownTimer();
        return Task.CompletedTask;
    }

    /// <summary>Pull-to-refresh, the filter button and a category change: the student asked for a new feed.</summary>
    [RelayCommand]
    private Task RefreshAsync()
    {
        IsRefreshing = false;
        return LoadFeedAsync(showSkeleton: true);
    }

    private async Task LoadFeedAsync(bool showSkeleton)
    {
        if (IsBusy) return;
        IsBusy = true;
        if (showSkeleton)
        {
            Listings.Clear();
            OnPropertyChanged(nameof(HasListings));
            IsLoading = true;
        }
        try
        {
            var page = await _listings.GetActiveFeedAsync(SelectedCategory);
            _cursor = page.NextCursor;
            _loadedAt = DateTimeOffset.UtcNow;
            _fetched.Clear();
            _fetched.AddRange(page.Items);
            ApplySearch();
        }
        finally
        {
            IsBusy = false;
            IsLoading = false;
            OnPropertyChanged(nameof(ShowEmptyState));
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (IsBusy || _cursor is null) return;
        IsBusy = true;
        IsLoadingMore = true;
        try
        {
            var page = await _listings.GetActiveFeedAsync(SelectedCategory, cursor: _cursor);
            _cursor = page.NextCursor;
            _fetched.AddRange(page.Items);
            ApplySearch();
        }
        finally
        {
            IsBusy = false;
            IsLoadingMore = false;
        }
    }

    [RelayCommand]
    private void SelectCategory(string? key)
    {
        SelectedCategory = string.IsNullOrEmpty(key) || key == "All"
            ? null
            : Enum.Parse<ItemCategory>(key);
    }

    [RelayCommand]
    private Task OpenListingAsync(Listing? listing) =>
        listing is null
            ? Task.CompletedTask
            : _nav.GoToAsync(Routes.ListingDetail, new Dictionary<string, object> { ["listingId"] = listing.Id });

    /// <summary>
    /// Rebuilds the bound collection from <see cref="_fetched"/> in one notification —
    /// see <see cref="ObservableRangeCollection{T}"/> for why that matters here.
    /// </summary>
    private void ApplySearch()
    {
        var q = SearchText.Trim();
        var matches = q.Length == 0
            ? (IEnumerable<Listing>)_fetched
            : _fetched.Where(l =>
                l.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                l.Description.Contains(q, StringComparison.OrdinalIgnoreCase));

        Listings.ReplaceAll(matches);
        OnPropertyChanged(nameof(HasListings));
        OnPropertyChanged(nameof(ShowEmptyState));
        TickCountdowns();
    }

    private void StartCountdownTimer()
    {
        if (_countdownTimer is not null) return;
        _countdownTimer = Application.Current?.Dispatcher.CreateTimer();
        if (_countdownTimer is null) return;
        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.Tick += (_, _) => TickCountdowns();
        _countdownTimer.Start();
    }

    private void StopCountdownTimer()
    {
        _countdownTimer?.Stop();
        _countdownTimer = null;
    }

    private void TickCountdowns()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var listing in Listings) listing.TickCountdown(now);
    }

    /// <summary>The category is part of the Firestore query, so changing it refetches.</summary>
    partial void OnSelectedCategoryChanged(ItemCategory? value) => _ = LoadFeedAsync(showSkeleton: true);

    /// <summary>Search filters what is already loaded. No network, no dropped keystrokes.</summary>
    partial void OnSearchTextChanged(string value) => ApplySearch();
}

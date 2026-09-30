using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NUTrade1.Core;
using NUTrade1.Services;

namespace NUTrade1.ViewModels;

/// <summary>
/// "Review Listing": the last look before anything is written or paid for.
///
/// Nothing reaches Firestore until the student taps Post Listing — the form hands this
/// screen the finished request, and backing out from here leaves no draft behind. Posting
/// saves the draft and replaces this screen with the payment one, so Back from payment
/// returns to the (now cleared) form, never to a second Post button for the same listing.
/// </summary>
public partial class ReviewListingViewModel : BaseViewModel, IQueryAttributable
{
    public const string RequestKey = "request";

    private readonly IListingService _listings;
    private readonly INavigationService _nav;
    private CreateAuctionRequest? _request;
    private bool _posted;

    public ReviewListingViewModel(IListingService listings, INavigationService nav)
    {
        _listings = listings;
        _nav = nav;
        Title = "Review Listing";
    }

    /// <summary>The request, shaped as the listing a buyer will see, so labels match the detail page.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartingBidDisplay))]
    [NotifyPropertyChangedFor(nameof(IncrementDisplay))]
    [NotifyPropertyChangedFor(nameof(PackageDisplay))]
    [NotifyPropertyChangedFor(nameof(PaymentDisplay))]
    [NotifyPropertyChangedFor(nameof(IsFree))]
    [NotifyPropertyChangedFor(nameof(ShowAuctionTerms))]
    [NotifyPropertyChangedFor(nameof(IsSwap))]
    [NotifyPropertyChangedFor(nameof(PriceCaption))]
    private Listing? _preview;

    public string StartingBidDisplay => Money.ToDisplay(Preview?.StartingBidCentavos ?? 0);

    public string IncrementDisplay => Money.ToDisplay(Preview?.MinIncrementCentavos ?? 0);

    public bool ShowAuctionTerms => Preview?.IsAuction == true;

    public bool IsSwap => Preview?.Kind == ListingKind.Swap;

    public string PriceCaption => Preview?.Kind == ListingKind.Standard ? "PRICE" : "STARTING BID";

    private long FeeCentavos => NUTradeConstants.FeeForPackage(Preview?.Package ?? ListingPackage.Free);

    public bool IsFree => FeeCentavos == 0;

    public string PackageDisplay => $"{Preview?.Package ?? ListingPackage.Free} – {Money.ToDisplay(FeeCentavos)}";

    public string PaymentDisplay => IsFree ? "None — first post is free" : "PayMongo (QR Ph)";

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue(RequestKey, out var value) || value is not CreateAuctionRequest request) return;

        _request = request;
        _posted = false;
        Preview = new Listing
        {
            Title = request.Title,
            Description = request.Description,
            Condition = request.Condition,
            Category = request.Category,
            CategoryOther = request.CategoryOther,
            Photos = request.PhotoLocalPaths.ToList(),
            CampusZone = request.CampusZone,
            CampusZoneOther = request.CampusZoneOther,
            Package = request.Package,
            Kind = request.Kind,
            StartingBidCentavos = request.StartingBidCentavos,
            MinIncrementCentavos = request.MinIncrementCentavos,
            CurrentHighestBidCentavos = request.StartingBidCentavos,
        };
    }

    public override async Task OnAppearingAsync()
    {
        // Reached with nothing to review (the OS restored this page on its own): go back
        // to the form rather than show an empty listing with a live Post button.
        if (_request is null) await _nav.GoBackAsync();
    }

    [RelayCommand]
    private async Task PostAsync()
    {
        if (_request is null || _posted || IsBusy) return;

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var result = await _listings.CreateDraftAsync(_request);
            if (!result.Succeeded || string.IsNullOrEmpty(result.Value))
            {
                ErrorMessage = result.Error ?? "Could not create the listing.";
                return;
            }

            _posted = true;

            // The photos are in Firebase Storage now; the local copies are just clutter.
            ImageCache.Delete(_request.PhotoLocalPaths);
            WeakReferenceMessenger.Default.Send(new ListingSubmittedMessage(result.Value));

            // Every package goes through the payment screen, free included. What was saved
            // is a draft, and only createQrPayment can move it on: for a first free auction
            // it queues it for approval straight away and the screen just confirms;
            // otherwise it mints the QR Ph code to pay. "../" swaps this screen out.
            await _nav.GoToAsync($"../{Routes.Payment}", new Dictionary<string, object>
            {
                ["listingId"] = result.Value,
                ["package"] = _request.Package,
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task EditAsync() => _nav.GoBackAsync();

    /// <summary>Opens the full-screen viewer, the same one buyers get, at the tapped photo.</summary>
    [RelayCommand]
    private Task OpenPhotoAsync(int index)
    {
        if (Preview is not { Photos.Count: > 0 } preview) return Task.CompletedTask;
        return _nav.GoToAsync(Routes.PhotoViewer, new Dictionary<string, object>
        {
            [PhotoViewerViewModel.PhotosKey] = preview.Photos.ToList(),
            [PhotoViewerViewModel.IndexKey] = index,
            [PhotoViewerViewModel.TitleKey] = preview.Title,
        });
    }
}

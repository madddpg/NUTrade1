using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NUTrade1.Core;
using NUTrade1.Services;

namespace NUTrade1.ViewModels;

public partial class PostTradeViewModel : BaseViewModel
{
    private readonly IListingService _listings;
    private readonly IStorageService _storage;
    private readonly INavigationService _nav;

    public PostTradeViewModel(IListingService listings, IStorageService storage, INavigationService nav)
    {
        _listings = listings;
        _storage = storage;
        _nav = nav;
        Title = "Create an auction";

        // Category and meetup location are left unset on purpose — both are required, and
        // a pre-picked value is a value nobody looked at. Condition keeps its default
        // because every value is a valid answer.
        SelectedConditionOption = ConditionOptions[1];

        WeakReferenceMessenger.Default.Register<PostTradeViewModel, ListingSubmittedMessage>(
            this, static (vm, _) => vm.Reset());
    }

    [ObservableProperty] private string _itemTitle = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private ItemCondition _condition;
    [ObservableProperty] private string _startingBidText = string.Empty;
    [ObservableProperty] private string _bidIncrementText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCategoryOther))]
    private ItemCategory _category;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMeetupOther))]
    private CampusZone _campusZone;

    /// <summary>The seller's own category, required once they pick "Other".</summary>
    [ObservableProperty] private string _categoryOther = string.Empty;

    /// <summary>The seller's own meetup place, required once they pick "Others".</summary>
    [ObservableProperty] private string _campusZoneOther = string.Empty;

    /// <summary>Reveals the "which category?" box; the enum alone is not an answer.</summary>
    public bool IsCategoryOther => Category == ItemCategory.Other;

    /// <summary>Reveals the "where exactly?" box.</summary>
    public bool IsMeetupOther => CampusZone == CampusZone.Other;

    // Starts on Additional and only moves to Free once the check below confirms the
    // student still has their free post — never the other way round.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FeeDisplay))]
    [NotifyPropertyChangedFor(nameof(PublishText))]
    private ListingPackage _selectedPackage = ListingPackage.Additional;

    /// <summary>The Free package is offered only until the student's first post has gone through.</summary>
    [ObservableProperty] private bool _isFreeAvailable;

    /// <summary>Explains why Free is missing, once we know it has been used.</summary>
    [ObservableProperty] private bool _showFreeUsedNote;

    /// <summary>Set once the student taps a package, so the free-post check never overrides their pick.</summary>
    private bool _packageChosenByUser;

    public ObservableCollection<string> PhotoPaths { get; } = new();

    public bool CanAddPhoto => PhotoPaths.Count < NUTradeConstants.MaxListingPhotos;
    public bool HasPhotos => PhotoPaths.Count > 0;

    public long FeeCentavos => NUTradeConstants.FeeForPackage(SelectedPackage);
    public string FeeDisplay => Money.ToDisplay(FeeCentavos);
    public string PublishText => FeeCentavos == 0 ? "Publish auction" : $"Continue to review · {FeeDisplay}";

    public List<EnumOption<ItemCategory>> CategoryOptions { get; } =
    [
        new() { Value = ItemCategory.Uniforms, Label = "Uniforms" },
        new() { Value = ItemCategory.Textbooks, Label = "Textbooks" },
        new() { Value = ItemCategory.AcademicSupplies, Label = "Academic Supplies" },
        new() { Value = ItemCategory.Other, Label = "Other" },
    ];

    public List<EnumOption<ItemCondition>> ConditionOptions { get; } =
    [
        new() { Value = ItemCondition.New, Label = "New" },
        new() { Value = ItemCondition.LikeNew, Label = "Like New" },
        new() { Value = ItemCondition.Good, Label = "Good" },
        new() { Value = ItemCondition.Worn, Label = "Worn" },
    ];

    public List<EnumOption<CampusZone>> ZoneOptions { get; } =
    [
        new() { Value = CampusZone.StudentLounge, Label = "Student Lounge" },
        new() { Value = CampusZone.Gymnasium, Label = "Gymnasium" },
        new() { Value = CampusZone.AccountingAndRegistrarOffice, Label = "Accounting and Registrar Office" },
        new() { Value = CampusZone.Itso, Label = "ITSO" },
        new() { Value = CampusZone.Sdao, Label = "SDAO" },
        new() { Value = CampusZone.Avr, Label = "AVR" },
        new() { Value = CampusZone.Other, Label = "Others" },
    ];

    [ObservableProperty] private EnumOption<ItemCategory>? _selectedCategoryOption;
    [ObservableProperty] private EnumOption<ItemCondition>? _selectedConditionOption;
    [ObservableProperty] private EnumOption<CampusZone>? _selectedZoneOption;

    partial void OnSelectedCategoryOptionChanged(EnumOption<ItemCategory>? value) => Category = value?.Value ?? ItemCategory.Unknown;
    partial void OnSelectedConditionOptionChanged(EnumOption<ItemCondition>? value) => Condition = value?.Value ?? ItemCondition.Unknown;
    partial void OnSelectedZoneOptionChanged(EnumOption<CampusZone>? value) => CampusZone = value?.Value ?? CampusZone.Unknown;

    [RelayCommand]
    private void SelectPackage(string? key)
    {
        if (!Enum.TryParse<ListingPackage>(key, out var p)) return;
        if (p == ListingPackage.Free && !IsFreeAvailable) return;

        _packageChosenByUser = true;
        SelectedPackage = p;
    }

    /// <summary>
    /// Re-checked every time the tab opens: posting the first listing, or having it
    /// rejected, changes the answer. createQrPayment enforces the same rule, so this only
    /// decides what the student is offered.
    /// </summary>
    public override async Task OnAppearingAsync()
    {
        bool used;
        try
        {
            used = await _listings.HasUsedFreePostAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Offline or a failed read: offer Free and let the server decide at checkout.
            used = false;
        }

        IsFreeAvailable = !used;
        ShowFreeUsedNote = used;

        if (used && SelectedPackage == ListingPackage.Free)
            SelectedPackage = ListingPackage.Additional;
        else if (!used && !_packageChosenByUser)
            SelectedPackage = ListingPackage.Free;
    }

    [RelayCommand]
    private async Task AddPhotoAsync()
    {
        if (!CanAddPhoto) return;
        try
        {
            var photos = await MediaPicker.Default.PickPhotosAsync();
            if (photos is null) return;
            foreach (var photo in photos)
            {
                if (!CanAddPhoto) break;
                var cachedPath = await ImageCache.CacheAsync(photo.FullPath);
                PhotoPaths.Add(cachedPath);
                RaisePhotoState();
            }
        }
        catch (FeatureNotSupportedException)
        {
            ErrorMessage = "Photo picker isn't available on this device.";
        }
        catch (PermissionException)
        {
            ErrorMessage = "Allow photo access to attach pictures.";
        }
    }

    [RelayCommand]
    private void RemovePhoto(string? path)
    {
        if (path is not null)
        {
            PhotoPaths.Remove(path);
            ImageCache.Delete(path);
        }
        RaisePhotoState();
    }

    private void RaisePhotoState()
    {
        OnPropertyChanged(nameof(CanAddPhoto));
        OnPropertyChanged(nameof(HasPhotos));
    }

    /// <summary>
    /// Checks the form and opens the review screen. Nothing is written yet — the draft is
    /// only created when the student taps Post Listing there.
    /// </summary>
    [RelayCommand]
    private async Task ContinueToReviewAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(ItemTitle))
        {
            ErrorMessage = "Give your item a name.";
            return;
        }
        if (PhotoPaths.Count < NUTradeConstants.MinListingPhotos)
        {
            ErrorMessage = "Add at least one photo.";
            return;
        }
        if (Category == ItemCategory.Unknown)
        {
            ErrorMessage = "Pick a category.";
            return;
        }
        if (IsCategoryOther && string.IsNullOrWhiteSpace(CategoryOther))
        {
            ErrorMessage = "Say which category your item belongs to.";
            return;
        }
        if (!long.TryParse(StartingBidText.Trim(), out var startingPesos) || startingPesos <= 0)
        {
            ErrorMessage = "Enter a valid starting bid.";
            return;
        }
        if (!long.TryParse(BidIncrementText.Trim(), out var incrementPesos) || incrementPesos <= 0)
        {
            ErrorMessage = "Enter a valid bid increment.";
            return;
        }
        if (CampusZone == CampusZone.Unknown)
        {
            ErrorMessage = "Pick a meetup location.";
            return;
        }
        if (IsMeetupOther && string.IsNullOrWhiteSpace(CampusZoneOther))
        {
            ErrorMessage = "Say where on campus you want to meet.";
            return;
        }

        var request = new CreateAuctionRequest
        {
            Title = ItemTitle.Trim(),
            Description = Description.Trim(),
            Condition = Condition,
            Category = Category,
            CategoryOther = IsCategoryOther ? CategoryOther.Trim() : string.Empty,
            PhotoLocalPaths = PhotoPaths.ToList(),
            CampusZone = CampusZone,
            CampusZoneOther = IsMeetupOther ? CampusZoneOther.Trim() : string.Empty,
            Package = SelectedPackage,
            StartingBidCentavos = Money.FromPesos(startingPesos),
            MinIncrementCentavos = Money.FromPesos(incrementPesos),
        };

        // Guards a double tap, which would otherwise stack two review screens.
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _nav.GoToAsync(Routes.ReviewListing, new Dictionary<string, object>
            {
                [ReviewListingViewModel.RequestKey] = request,
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Clears the form once its listing has been saved, so the next one starts blank. The
    /// local photos are not deleted here: the review screen already removed them after upload.
    /// </summary>
    private void Reset()
    {
        ItemTitle = string.Empty;
        Description = string.Empty;
        StartingBidText = string.Empty;
        BidIncrementText = string.Empty;
        CategoryOther = string.Empty;
        CampusZoneOther = string.Empty;
        SelectedCategoryOption = null;
        SelectedZoneOption = null;
        SelectedConditionOption = ConditionOptions[1];
        PhotoPaths.Clear();
        RaisePhotoState();
        _packageChosenByUser = false;
    }
}

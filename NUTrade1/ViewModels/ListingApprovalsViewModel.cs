using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;

namespace NUTrade1.ViewModels;

/// <summary>One listing in the admin queue, with its seller's name resolved for display.</summary>
public sealed class PendingListingViewModel
{
    public PendingListingViewModel(Listing listing, string sellerName)
    {
        Listing = listing;
        SellerName = sellerName;
    }

    public Listing Listing { get; }
    public string SellerName { get; }

    public string Title => Listing.Title;
    public string Description => Listing.Description;
    public string? CoverPhoto => Listing.Photos.FirstOrDefault();
    public int PhotoCount => Listing.Photos.Count;
    public string StartingBidDisplay => Money.ToDisplay(Listing.StartingBidCentavos);

    public string PackageDisplay
    {
        get
        {
            var package = Listing.PaidPackage ?? Listing.Package;
            var fee = NUTradeConstants.FeeForPackage(package);
            return fee == 0 ? $"{package} · free" : $"{package} · {Money.ToDisplay(fee)} paid";
        }
    }

    public DateTimeOffset SubmittedAt => Listing.SubmittedForApprovalAt ?? Listing.CreatedAt;
}

/// <summary>
/// The admin approval queue: every listing whose fee has cleared waits here until an
/// admin puts it live or turns it down. Reached from the admin card on Profile.
/// </summary>
public partial class ListingApprovalsViewModel : BaseViewModel
{
    private readonly IListingModerationService _moderation;
    private readonly IUserService _users;
    private readonly IAuthService _auth;

    public ListingApprovalsViewModel(IListingModerationService moderation, IUserService users, IAuthService auth)
    {
        _moderation = moderation;
        _users = users;
        _auth = auth;
        Title = "Listing approvals";
    }

    public ObservableCollection<PendingListingViewModel> Pending { get; } = new();

    public bool HasPending => Pending.Count > 0;

    /// <summary>"All caught up" — only once a load has finished, so it never flashes up mid-fetch.</summary>
    public bool ShowEmptyState => !HasPending && !IsBusy && !IsLoading && ErrorMessage is null;

    /// <summary>ErrorMessage lives on BaseViewModel now, so the empty state is re-evaluated here.</summary>
    protected override void OnErrorMessageChangedCore(string? value) => OnPropertyChanged(nameof(ShowEmptyState));

    // Bones for the queue on a first visit; reopened with requests on screen, it refreshes quietly.
    public override Task OnAppearingAsync() => LoadQueueAsync(showSkeleton: Pending.Count == 0);

    /// <summary>Refresh, by pull or button: the admin asked, so the queue reloads in bones.</summary>
    [RelayCommand]
    private Task LoadAsync()
    {
        IsRefreshing = false;
        return LoadQueueAsync(showSkeleton: true);
    }

    private async Task LoadQueueAsync(bool showSkeleton)
    {
        if (!_auth.IsAdmin)
        {
            ErrorMessage = "Only NUTrade admins can review listings.";
            return;
        }

        IsBusy = true;
        IsLoading = showSkeleton;
        RaiseListState();
        try
        {
            var listings = await _moderation.GetPendingListingsAsync();

            // One profile read per seller, not per listing — a student often has several queued.
            var names = new Dictionary<string, string>();
            foreach (var uid in listings.Select(l => l.OwnerUid).Distinct())
                names[uid] = (await _users.GetProfileAsync(uid))?.DisplayName ?? "NU student";

            Pending.Clear();
            foreach (var listing in listings)
                Pending.Add(new PendingListingViewModel(listing, names.GetValueOrDefault(listing.OwnerUid, "NU student")));

            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = "Couldn't load the approval queue. Tap Refresh to try again.";
        }
        finally
        {
            IsBusy = false;
            IsLoading = false;
            RaiseListState();
        }
    }

    [RelayCommand]
    private Task ApproveAsync(PendingListingViewModel? item) =>
        DecideAsync(item, i => _moderation.ApproveListingAsync(i.Listing.Id), $"Approved “{item?.Title}”.");

    [RelayCommand]
    private async Task RejectAsync(PendingListingViewModel? item)
    {
        if (item is null || Shell.Current is not { } shell) return;

        // Null means the admin backed out; an empty string is a rejection with no note.
        var reason = await shell.DisplayPromptAsync(
            "Reject listing",
            $"Tell the seller why “{item.Title}” wasn't approved (optional).",
            accept: "Reject",
            cancel: "Cancel",
            placeholder: "e.g. Photos don't show the item",
            maxLength: 300);
        if (reason is null) return;

        await DecideAsync(item, i => _moderation.RejectListingAsync(i.Listing.Id, reason.Trim()), $"Rejected “{item.Title}”.");
    }

    private async Task DecideAsync(
        PendingListingViewModel? item,
        Func<PendingListingViewModel, Task<OperationResult>> decide,
        string doneMessage)
    {
        if (item is null || IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await decide(item);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Error;
                return;
            }

            Pending.Remove(item);
            ErrorMessage = null;
            RaiseListState();
            StatusMessage = doneMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RaiseListState()
    {
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(ShowEmptyState));
    }
}

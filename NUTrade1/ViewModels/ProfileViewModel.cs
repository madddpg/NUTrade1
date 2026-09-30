using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;
using NUTrade1.Services;

namespace NUTrade1.ViewModels;

public partial class ProfileViewModel : BaseViewModel
{
    private readonly IAuthService _auth;
    private readonly IUserService _users;
    private readonly IListingService _listings;
    private readonly IBidService _bids;
    private readonly IListingModerationService _moderation;
    private readonly IWalletService _wallet;
    private readonly IPayoutModerationService _payouts;
    private readonly INavigationService _nav;

    public ProfileViewModel(
        IAuthService auth,
        IUserService users,
        IListingService listings,
        IBidService bids,
        IListingModerationService moderation,
        IWalletService wallet,
        IPayoutModerationService payouts,
        INavigationService nav)
    {
        _auth = auth;
        _users = users;
        _listings = listings;
        _bids = bids;
        _moderation = moderation;
        _wallet = wallet;
        _payouts = payouts;
        _nav = nav;
        Title = "Profile";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgramActionText))]
    private UserProfile? _profile;

    /// <summary>The link beside the program under the name: a prompt until one is chosen.</summary>
    public string ProgramActionText => string.IsNullOrEmpty(Profile?.Program) ? "Set your program" : "Change";
    [ObservableProperty] private int _activeCount;
    [ObservableProperty] private int _bidsReceivedCount;
    [ObservableProperty] private string _ratingText = "New";

    /// <summary>Listings waiting in the admin queue — only loaded for admins.</summary>
    [ObservableProperty] private int _pendingApprovalCount;

    /// <summary>Payout requests waiting in the admin queue — only loaded for admins.</summary>
    [ObservableProperty] private int _pendingPayoutCount;

    /// <summary>The student's own spendable balance, shown on the wallet card.</summary>
    [ObservableProperty] private string _walletBalanceDisplay = "₱0.00";

    /// <summary>Confirmed-email badge. The Shell gate keeps unverified accounts out of
    /// this page entirely, so in practice this is true whenever Profile is on screen —
    /// it can only go false if an admin revokes the account mid-session.</summary>
    public bool IsVerified => _auth.IsVerified;

    /// <summary>Shows the "Listing approvals" entry. The Functions re-check the claim on every decision.</summary>
    public bool IsAdmin => _auth.IsAdmin;

    public ObservableRangeCollection<Listing> MyListings { get; } = new();
    public ObservableRangeCollection<IncomingBidViewModel> IncomingBids { get; } = new();

    public bool HasListings => MyListings.Count > 0;
    public bool HasIncomingBids => IncomingBids.Count > 0;


    public override async Task OnAppearingAsync()
    {
        // The skeleton only while there is no profile on screen yet. Coming back to the
        // tab, or reloading after a bid decision, refreshes behind what is already shown.
        IsLoading = Profile is null;
        try
        {
            await LoadAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadAsync()
    {
        // An admin may have revoked this account since the session started, and a token
        // refresh is the only way that reaches the client. Not forced: RefreshClaimsAsync
        // skips the network if it already refreshed within the last minute, so reopening
        // this tab is not a round trip every time.
        await _auth.RefreshClaimsAsync();
        OnPropertyChanged(nameof(IsVerified));
        OnPropertyChanged(nameof(IsAdmin));

        // The profile, the listings and the approval queue do not depend on each other, so
        // they go out together rather than as three round trips in a row.
        var profileTask = _users.GetCurrentProfileAsync();
        var listingsTask = _listings.GetMyListingsAsync();
        var walletTask = _wallet.GetWalletAsync();
        var pendingTask = IsAdmin ? _moderation.GetPendingListingsAsync() : null;
        var payoutsTask = IsAdmin ? _payouts.GetPendingPayoutsAsync() : null;

        Profile = await profileTask;
        RatingText = Profile?.Rating is { } r ? r.ToString("0.0") : "New";

        var mine = await listingsTask;
        MyListings.ReplaceAll(mine);
        ActiveCount = mine.Count(l => l.Status is ListingStatus.Active or ListingStatus.PendingPayment
            or ListingStatus.PendingApproval or ListingStatus.Matched);
        OnPropertyChanged(nameof(HasListings));

        // Bids live in a subcollection per listing, so this is one query per listing and
        // there is no way around that from the client. Awaiting them in the loop made it
        // N *sequential* round trips, though — a seller with eight listings sat on a blank
        // Profile for eight times the latency. Fanned out, it costs one.
        var bidQueries = mine.Select(listing => _bids.GetIncomingBidsAsync(listing.Id)).ToArray();
        var bidResults = await Task.WhenAll(bidQueries);

        // WhenAll keeps the input order, so bids stay grouped under their own listing.
        IncomingBids.ReplaceAll(
            mine.Zip(bidResults, (listing, bids) => bids.Select(bid => new IncomingBidViewModel(bid, listing)))
                .SelectMany(x => x));
        BidsReceivedCount = IncomingBids.Count;
        OnPropertyChanged(nameof(HasIncomingBids));

        // The wallet and the admin queues are extras on this page, so they are read last:
        // if one of them fails, the profile, listings and bids above are already on screen
        // and only the balance or a queue count is missing.
        WalletBalanceDisplay = (await walletTask).BalanceDisplay;
        if (pendingTask is not null) PendingApprovalCount = (await pendingTask).Count;
        if (payoutsTask is not null) PendingPayoutCount = (await payoutsTask).Count;
    }

    /// <summary>
    /// Registration asks for the program, but accounts made before it did — and Google
    /// sign-ins — have none, and anyone can pick the wrong one. This is where it's fixed.
    /// </summary>
    [RelayCommand]
    private async Task ChooseProgramAsync()
    {
        if (IsBusy || Shell.Current is not { } shell) return;

        var choice = await shell.DisplayActionSheetAsync(
            "Your program", "Cancel", null, NUTradeConstants.Programs.ToArray());
        if (choice is null || !NUTradeConstants.Programs.Contains(choice) || choice == Profile?.Program) return;

        IsBusy = true;
        try
        {
            var result = await _users.SetProgramAsync(choice);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Error ?? "Couldn't save your program.";
                return;
            }
            Profile = await _users.GetCurrentProfileAsync();
            InfoMessage = $"Program set to {choice}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task NewListingAsync() => _nav.GoToAsync(Routes.PostTradeTab);

    [RelayCommand]
    private Task OpenApprovalsAsync() => _nav.GoToAsync(Routes.ListingApprovals);

    [RelayCommand]
    private Task OpenWalletAsync() => _nav.GoToAsync(Routes.Wallet);

    [RelayCommand]
    private Task OpenPayoutsAsync() => _nav.GoToAsync(Routes.Payouts);

    /// <summary>
    /// Reopens the payment screen for a listing that never finished posting — a draft, or
    /// one waiting on its fee. createQrPayment asks PayMongo first, so a fee that was paid
    /// but never confirmed is settled rather than charged again.
    /// </summary>
    [RelayCommand]
    private async Task OpenListingAsync(Listing? listing)
    {
        if (listing is null || listing.Status is not (ListingStatus.Draft or ListingStatus.PendingPayment)) return;

        var package = listing.Package;
        if (package == ListingPackage.Free && await _listings.HasUsedFreePostAsync())
            package = ListingPackage.Additional;

        await _nav.GoToAsync(Routes.Payment, new Dictionary<string, object>
        {
            ["listingId"] = listing.Id,
            ["package"] = package,
        });
    }

    // Both actions take the whole row, not just a bid id: bids live in a subcollection,
    // so the Function needs the listing id to address one.
    [RelayCommand]
    private Task ApproveAsync(IncomingBidViewModel? item) => ActOnBidAsync(item, _bids.ApproveBidAsync);

    [RelayCommand]
    private Task DeclineAsync(IncomingBidViewModel? item) => ActOnBidAsync(item, _bids.DeclineBidAsync);

    private async Task ActOnBidAsync(
        IncomingBidViewModel? item,
        Func<string, string, CancellationToken, Task<OperationResult>> action)
    {
        if (item is null || IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await action(item.Listing.Id, item.Bid.Id, CancellationToken.None);
            ErrorMessage = result.Succeeded ? null : result.Error;
        }
        finally
        {
            IsBusy = false;
        }

        await OnAppearingAsync();
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        await _auth.SignOutAsync();
        await _nav.ResetToRootAsync();
    }
}

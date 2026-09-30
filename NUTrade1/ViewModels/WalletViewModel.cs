using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;
using NUTrade1.Services;

namespace NUTrade1.ViewModels;

/// <summary>
/// The student's balance, the ledger behind it, and cashing out.
///
/// Nothing here can move money: the balance changes as a consequence of a trade settling,
/// server-side. The one action is a payout request, and it takes the whole balance —
/// partial payouts are more states for an admin to reconcile by hand than they are worth.
/// </summary>
public partial class WalletViewModel : BaseViewModel
{
    /// <summary>Mirrors MIN_PAYOUT_CENTAVOS in functions/src/constants.ts.</summary>
    private const long MinPayoutCentavos = 10_000;

    private readonly IWalletService _wallet;

    public WalletViewModel(IWalletService wallet)
    {
        _wallet = wallet;
        Title = "Wallet";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BalanceDisplay))]
    [NotifyPropertyChangedFor(nameof(CanRequestPayout))]
    [NotifyPropertyChangedFor(nameof(PayoutHint))]
    private long _balanceCentavos;

    public ObservableRangeCollection<LedgerEntry> Ledger { get; } = new();
    public ObservableRangeCollection<PayoutRequest> Payouts { get; } = new();

    public bool HasLedger => Ledger.Count > 0;
    public bool HasPayouts => Payouts.Count > 0;

    public string BalanceDisplay => Money.ToDisplay(BalanceCentavos);

    public bool CanRequestPayout => BalanceCentavos >= MinPayoutCentavos;

    public string PayoutHint => BalanceCentavos >= MinPayoutCentavos
        ? "An admin sends this to your GCash or Maya by hand, usually within a day."
        : $"You need at least {Money.ToDisplay(MinPayoutCentavos)} to cash out.";

    // ---- The payout form -----------------------------------------------------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormOpen))]
    private bool _isRequestingPayout;

    [ObservableProperty] private string _accountName = string.Empty;
    [ObservableProperty] private string _accountNumber = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGCash))]
    [NotifyPropertyChangedFor(nameof(IsMaya))]
    private PayoutMethod _method = PayoutMethod.GCash;

    public bool IsFormOpen => IsRequestingPayout;
    public bool IsGCash => Method == PayoutMethod.GCash;
    public bool IsMaya => Method == PayoutMethod.Maya;

    /// <summary>Set once the wallet has been on screen; after that, reopening it refreshes quietly.</summary>
    private bool _loaded;

    public override Task OnAppearingAsync() => LoadWalletAsync(showSkeleton: !_loaded);

    /// <summary>Pull-to-refresh: the student asked, so the wallet reloads in bones.</summary>
    [RelayCommand]
    private Task LoadAsync()
    {
        IsRefreshing = false;
        return LoadWalletAsync(showSkeleton: true);
    }

    private async Task LoadWalletAsync(bool showSkeleton)
    {
        IsBusy = true;
        IsLoading = showSkeleton;
        try
        {
            // Three independent reads — no reason to make the student wait for them in turn.
            var walletTask = _wallet.GetWalletAsync();
            var ledgerTask = _wallet.GetLedgerAsync();
            var payoutsTask = _wallet.GetMyPayoutsAsync();

            BalanceCentavos = (await walletTask).BalanceCentavos;
            Ledger.ReplaceAll(await ledgerTask);
            Payouts.ReplaceAll(await payoutsTask);
            RaiseListState();
            _loaded = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = "Couldn't load your wallet. Pull to refresh to try again.";
        }
        finally
        {
            IsBusy = false;
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectMethod(string? key)
    {
        if (Enum.TryParse<PayoutMethod>(key, ignoreCase: true, out var method)) Method = method;
    }

    [RelayCommand]
    private void OpenPayoutForm()
    {
        if (!CanRequestPayout) return;
        IsRequestingPayout = true;
    }

    [RelayCommand]
    private void CancelPayout()
    {
        IsRequestingPayout = false;
        AccountName = AccountNumber = string.Empty;
    }

    [RelayCommand]
    private async Task SubmitPayoutAsync()
    {
        if (IsBusy) return;

        if (string.IsNullOrWhiteSpace(AccountName))
        {
            ErrorMessage = "Enter the name on the account.";
            return;
        }

        // Shape-check here so an obvious typo doesn't cost a round trip; the Function
        // re-checks it properly, including the +63 form.
        var digits = new string(AccountNumber.Where(char.IsDigit).ToArray());
        if (!(digits.Length == 11 && digits.StartsWith("09")))
        {
            ErrorMessage = "Enter the 11-digit mobile number, starting 09.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _wallet.RequestPayoutAsync(
                new PayoutDestination(Method, AccountName.Trim(), digits));
            if (!result.Succeeded)
            {
                ErrorMessage = result.Error ?? "Could not request the payout. Try again.";
                return;
            }

            IsRequestingPayout = false;
            AccountName = AccountNumber = string.Empty;
            InfoMessage = "Payout requested. An admin will send it shortly.";
        }
        finally
        {
            IsBusy = false;
        }

        // Outside the busy block: the balance has moved to zero and a request has
        // appeared, and both need re-reading — quietly, the wallet is already on screen.
        await LoadWalletAsync(showSkeleton: false);
    }

    private void RaiseListState()
    {
        OnPropertyChanged(nameof(HasLedger));
        OnPropertyChanged(nameof(HasPayouts));
    }
}

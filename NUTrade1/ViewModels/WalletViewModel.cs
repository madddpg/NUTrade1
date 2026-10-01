using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;
using NUTrade1.Services;

namespace NUTrade1.ViewModels;

/// <summary>
/// Bid credit and the lines that explain it. Nothing here can cash the balance out:
/// the next bid spends it, and only a settled deposit adds to it.
/// </summary>
public partial class WalletViewModel : BaseViewModel
{
    private readonly IWalletService _wallet;
    private readonly INavigationService _nav;
    private bool _loaded;

    public WalletViewModel(IWalletService wallet, INavigationService nav)
    {
        _wallet = wallet;
        _nav = nav;
        Title = "Bid credit";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BalanceDisplay))]
    private long _balanceCentavos;

    public ObservableRangeCollection<LedgerEntry> Ledger { get; } = new();

    public bool HasLedger => Ledger.Count > 0;

    public string BalanceDisplay => Money.ToDisplay(BalanceCentavos);

    public override Task OnAppearingAsync() => LoadWalletAsync(showSkeleton: !_loaded);

    [RelayCommand]
    private Task OpenReceiptAsync(LedgerEntry? entry) =>
        entry is null
            ? Task.CompletedTask
            : ReceiptViewModel.OpenAsync(_nav, Receipts.Ledger(entry));

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
            var walletTask = _wallet.GetWalletAsync();
            var ledgerTask = _wallet.GetLedgerAsync();

            BalanceCentavos = (await walletTask).BalanceCentavos;
            Ledger.ReplaceAll(await ledgerTask);
            OnPropertyChanged(nameof(HasLedger));
            _loaded = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = "Couldn't load your bid credit. Pull to refresh to try again.";
        }
        finally
        {
            IsBusy = false;
            IsLoading = false;
        }
    }
}

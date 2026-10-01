using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;
using NUTrade1.Services;

namespace NUTrade1.ViewModels;

/// <summary>
/// Scan to place your bid: the QR for a commitment deposit, and the wait for it to land.
///
/// The polling here is not a convenience — it is how bids get placed. PayMongo's webhook
/// has never arrived in this project, so `checkBidDeposit` asking PayMongo directly is
/// what writes the bid. Leaving this screen does not lose the bid: the server's expiry
/// sweep asks PayMongo too, so a deposit paid after the student walked away still commits.
/// </summary>
[QueryProperty(nameof(BidIntentId), "bidIntentId")]
public partial class BidDepositViewModel : BaseViewModel
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IDepositService _deposits;
    private readonly INavigationService _nav;
    private CancellationTokenSource? _polling;
    private SecondClock? _expiryClock;
    private bool _announced;

    public BidDepositViewModel(IDepositService deposits, INavigationService nav)
    {
        _deposits = deposits;
        _nav = nav;
        Title = "Confirm your bid";
    }

    [ObservableProperty] private string? _bidIntentId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DepositDisplay))]
    [NotifyPropertyChangedFor(nameof(BidDisplay))]
    [NotifyPropertyChangedFor(nameof(ItemTitle))]
    [NotifyPropertyChangedFor(nameof(IsAwaiting))]
    [NotifyPropertyChangedFor(nameof(IsCommitted))]
    [NotifyPropertyChangedFor(nameof(IsReturned))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(DepositExplainer))]
    [NotifyPropertyChangedFor(nameof(ExpiryText))]
    private BidDeposit? _deposit;

    public string ItemTitle => Deposit?.ListingTitle ?? string.Empty;
    public string DepositDisplay => Deposit?.DepositDisplay ?? "₱0.00";
    public string BidDisplay => Deposit?.AmountDisplay ?? "₱0.00";
    /// <summary>What "Save QR" calls the picture, so it is easy to find in the gallery.</summary>
    public string QrFileName => $"nutrade-bid-deposit-{DateTime.Now:yyyyMMdd-HHmm}";

    public bool IsAwaiting => Deposit?.IsAwaitingPayment ?? true;
    public bool IsCommitted => Deposit?.IsCommitted ?? false;

    /// <summary>The money came straight back — usually a bid that went stale while unpaid.</summary>
    public bool IsReturned => Deposit?.Status == DepositStatus.RefundedToBuyer;

    public string StatusText => Deposit?.Status switch
    {
        DepositStatus.LockedInEscrow => "Your bid is live. The deposit stays a promise until the auction ends.",
        DepositStatus.RefundedToBuyer => Deposit.StatusDisplay,
        DepositStatus.Expired => "The code expired before it was paid, so no bid was placed.",
        _ => Deposit?.CreditAppliedCentavos > 0
            ? $"Scan {Deposit.QrDueDisplay} with GCash, Maya or any bank app. Your bid credit is already applied."
            : "Scan the code with GCash, Maya or any bank app. Your bid goes live the moment it clears.",
    };

    /// <summary>
    /// Said plainly and before they pay: this is a deposit, not a fee, and there is exactly
    /// one way to lose it.
    /// </summary>
    public string DepositExplainer => Deposit is { } d
        ? CreditSentence(d) +
          $"{d.DepositDisplay} is {d.DepositPercent}% of your {d.AmountDisplay} bid. " +
          "You pay the full price in person if you win. This comes back as bid credit if you show up, lose, or are outbid. " +
          "You lose it only by winning and not showing up — then the seller gets it."
        : string.Empty;

    private static string CreditSentence(BidDeposit deposit) =>
        deposit.CreditAppliedCentavos <= 0
            ? string.Empty
            : deposit.QrDueCentavos <= 0
                ? $"{deposit.CreditAppliedDisplay} of your bid credit covered this deposit. "
                : $"{deposit.CreditAppliedDisplay} of your bid credit is already applied. Scan {deposit.QrDueDisplay} for the rest. ";

    public string ExpiryText => Deposit?.QrExpiresAt is { } at
        ? $"Expires in {CountdownClock.Format(at - DateTimeOffset.UtcNow)}"
        : string.Empty;

    public override async Task OnAppearingAsync()
    {
        if (string.IsNullOrEmpty(BidIntentId)) return;

        // The QR card in bones until the first check brings the deposit back. Later checks
        // (the poll, "check now") leave the card on screen.
        IsLoading = Deposit is null;
        try
        {
            await CheckAsync();
        }
        finally
        {
            IsLoading = false;
        }
        _expiryClock ??= new SecondClock(() => OnPropertyChanged(nameof(ExpiryText)));
        StartPolling();
    }

    public override Task OnDisappearingAsync()
    {
        StopPolling();
        _expiryClock?.Dispose();
        _expiryClock = null;
        return Task.CompletedTask;
    }

    /// <summary>"I've paid — check now", and the tick behind the automatic poll.</summary>
    [RelayCommand]
    private async Task CheckAsync()
    {
        if (BidIntentId is not { Length: > 0 } id || IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await _deposits.CheckDepositAsync(id);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Error;
                return;
            }

            Deposit = result.Value;
            if (Deposit is { IsResolved: true } or { IsCommitted: true }) StopPolling();
            if (Deposit is { IsCommitted: true }) Announce();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task OpenReceiptAsync()
    {
        if (Deposit is not { } deposit) return Task.CompletedTask;
        return ReceiptViewModel.OpenAsync(_nav, Receipts.BidDeposit(
            deposit.Id,
            string.IsNullOrWhiteSpace(deposit.ListingTitle) ? "Listing" : deposit.ListingTitle,
            deposit.DepositCentavos,
            deposit.AmountCentavos,
            deposit.ResolvedAt ?? deposit.CreatedAt ?? DateTimeOffset.UtcNow,
            deposit.CreditAppliedCentavos));
    }

    [RelayCommand]
    private Task BackToListingAsync() => _nav.GoBackAsync();

    private void Announce()
    {
        if (_announced || Deposit is not { } d) return;
        _announced = true;
        InfoMessage = $"Bid placed at {d.AmountDisplay}.";
    }

    private void StartPolling()
    {
        if (_polling is not null || Deposit is { IsAwaitingPayment: false }) return;
        _polling = new CancellationTokenSource();
        _ = PollAsync(_polling.Token);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, ct);
                await CheckAsync();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // A failed poll is retried on the next tick; never let it end the loop.
            }
        }
    }

    private void StopPolling()
    {
        _polling?.Cancel();
        _polling?.Dispose();
        _polling = null;
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;

namespace NUTrade1.ViewModels;

/// <summary>
/// The admin payout queue: every student who has asked for their balance, oldest first.
///
/// NUTrade does not move money itself — an admin sends it through GCash or Maya by hand
/// and then records that here. The balance was already debited when the student asked, so
/// "Mark sent" only closes the record; declining is what puts the money back.
/// </summary>
public partial class PayoutsViewModel : BaseViewModel
{
    private readonly IPayoutModerationService _payouts;
    private readonly IUserService _users;
    private readonly IAuthService _auth;

    public PayoutsViewModel(IPayoutModerationService payouts, IUserService users, IAuthService auth)
    {
        _payouts = payouts;
        _users = users;
        _auth = auth;
        Title = "Payout requests";
    }

    public ObservableCollection<PendingPayoutViewModel> Pending { get; } = new();

    public bool HasPending => Pending.Count > 0;

    /// <summary>"All caught up" — only once a load has finished, so it never flashes up mid-fetch.</summary>
    public bool ShowEmptyState => !HasPending && !IsBusy && !IsLoading && ErrorMessage is null;

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
            ErrorMessage = "Only NUTrade admins can review payouts.";
            return;
        }

        IsBusy = true;
        IsLoading = showSkeleton;
        RaiseListState();
        try
        {
            var requests = await _payouts.GetPendingPayoutsAsync();

            // One profile read per student, not per request.
            var names = new Dictionary<string, string>();
            foreach (var uid in requests.Select(r => r.Uid).Distinct())
                names[uid] = (await _users.GetProfileAsync(uid))?.DisplayName ?? "NU student";

            Pending.Clear();
            foreach (var request in requests)
                Pending.Add(new PendingPayoutViewModel(request, names.GetValueOrDefault(request.Uid, "NU student")));

            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = "Couldn't load the payout queue. Tap Refresh to try again.";
        }
        finally
        {
            IsBusy = false;
            IsLoading = false;
            RaiseListState();
        }
    }

    [RelayCommand]
    private async Task MarkPaidAsync(PendingPayoutViewModel? item)
    {
        if (item is null || Shell.Current is not { } shell) return;

        // Worth one confirmation: this is the admin asserting real money has left their
        // own GCash, and there is no way to un-assert it.
        var confirmed = await shell.DisplayAlert(
            "Mark as sent",
            $"Confirm you have sent {item.AmountDisplay} to {item.Request.AccountName} ({item.Request.AccountNumber}).",
            "I've sent it",
            "Cancel");
        if (!confirmed) return;

        await DecideAsync(item, i => _payouts.MarkPaidAsync(i.Request.Id), $"Marked {item.AmountDisplay} as sent.");
    }

    [RelayCommand]
    private async Task DeclineAsync(PendingPayoutViewModel? item)
    {
        if (item is null || Shell.Current is not { } shell) return;

        // Null means the admin backed out; an empty string is a decline with no note.
        var reason = await shell.DisplayPromptAsync(
            "Decline payout",
            $"Tell {item.StudentName} why (optional). The {item.AmountDisplay} goes back to their balance.",
            accept: "Decline",
            cancel: "Cancel",
            placeholder: "e.g. Account name doesn't match",
            maxLength: 200);
        if (reason is null) return;

        await DecideAsync(
            item,
            i => _payouts.DeclineAsync(i.Request.Id, reason.Trim()),
            $"Declined — {item.AmountDisplay} returned.");
    }

    private async Task DecideAsync(
        PendingPayoutViewModel? item,
        Func<PendingPayoutViewModel, Task<OperationResult>> decide,
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

/// <summary>One request in the admin queue, with the student's name resolved for display.</summary>
public sealed class PendingPayoutViewModel
{
    public PendingPayoutViewModel(PayoutRequest request, string studentName)
    {
        Request = request;
        StudentName = studentName;
    }

    public PayoutRequest Request { get; }
    public string StudentName { get; }

    public string AmountDisplay => Request.AmountDisplay;
    public string MethodDisplay => Request.Method == PayoutMethod.Maya ? "Maya" : "GCash";
    public string DestinationDisplay => $"{MethodDisplay} · {Request.AccountNumber} · {Request.AccountName}";
    public DateTimeOffset RequestedAt => Request.CreatedAt ?? DateTimeOffset.UtcNow;
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;
using NUTrade1.Services;

namespace NUTrade1.ViewModels;

/// <summary>
/// The last step of registration: enter the six-digit code mailed to the address the
/// student signed up with. Passing it grants the <c>verified</c> claim, which is what
/// every privileged write actually checks.
/// </summary>
public partial class VerifyEmailViewModel : BaseViewModel
{
    private readonly IEmailVerificationService _verification;
    private readonly IAuthService _auth;
    private readonly INavigationService _nav;
    private IDispatcherTimer? _cooldownTimer;

    public VerifyEmailViewModel(
        IEmailVerificationService verification, IAuthService auth, INavigationService nav)
    {
        _verification = verification;
        _auth = auth;
        _nav = nav;
        Title = "Confirm your email";
    }

    [ObservableProperty] private string _code = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Message))]
    private string _sentTo = string.Empty;

    /// <summary>
    /// Whether the code sheet is up. It opens itself as soon as a code is on its way, so
    /// the common path still has nothing to press; closing it leaves the explanation and a
    /// button to open it again.
    /// </summary>
    [ObservableProperty] private bool _isCodeModalOpen;

    public string Message => $"We sent a 6-digit code to {SentTo}. Enter it to finish setting up your account.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResendText))]
    [NotifyPropertyChangedFor(nameof(CanResend))]
    private int _resendInSeconds;

    public bool CanResend => ResendInSeconds <= 0;

    public string ResendText => CanResend ? "Send another code" : $"Send another code in {ResendInSeconds}s";

    /// <summary>A six-digit code is the only thing worth submitting, so gate the button on it.</summary>
    public bool CanSubmit => Code.Trim().Length == 6;

    public override async Task OnAppearingAsync()
    {
        SentTo = _auth.CurrentEmail ?? string.Empty;

        // Send on arrival so the common path is "open the app, read the mail, type six
        // digits" with nothing to press first.
        await RequestCodeAsync();
    }

    public override Task OnDisappearingAsync()
    {
        StopCooldown();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RequestCodeAsync()
    {
        if (IsBusy || !CanResend) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _verification.SendCodeAsync();
            if (!result.Succeeded)
            {
                ErrorMessage = result.Error ?? "Could not send the code. Try again.";
                return;
            }

            SentTo = result.Value!.SentTo;
            IsCodeModalOpen = true;
            StartCooldown(result.Value.ResendAfterSeconds);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsBusy) return;
        ErrorMessage = null;

        var entered = Code.Trim();
        if (entered.Length != 6 || !entered.All(char.IsDigit))
        {
            ErrorMessage = "Enter the 6-digit code from your email.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _verification.VerifyCodeAsync(entered);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Error ?? "That code isn't right.";
                Code = string.Empty;
                return;
            }

            // VerifyCodeAsync already refreshed the ID token, so the Shell gate will now
            // see a verified account and move on to profile setup.
            StopCooldown();
            await _nav.ResetToRootAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenCodeModal() => IsCodeModalOpen = true;

    [RelayCommand]
    private void CloseCodeModal() => IsCodeModalOpen = false;

    [RelayCommand]
    private async Task UseAnotherAccountAsync()
    {
        StopCooldown();
        await _auth.SignOutAsync();
        await _nav.ResetToRootAsync();
    }

    private void StartCooldown(int seconds)
    {
        StopCooldown();
        if (seconds <= 0) return;

        ResendInSeconds = seconds;
        _cooldownTimer = Application.Current?.Dispatcher.CreateTimer();
        if (_cooldownTimer is null) return;

        _cooldownTimer.Interval = TimeSpan.FromSeconds(1);
        _cooldownTimer.Tick += (_, _) =>
        {
            ResendInSeconds--;
            if (ResendInSeconds <= 0) StopCooldown();
        };
        _cooldownTimer.Start();
    }

    private void StopCooldown()
    {
        _cooldownTimer?.Stop();
        _cooldownTimer = null;
        ResendInSeconds = 0;
    }

    partial void OnCodeChanged(string value) => OnPropertyChanged(nameof(CanSubmit));
}

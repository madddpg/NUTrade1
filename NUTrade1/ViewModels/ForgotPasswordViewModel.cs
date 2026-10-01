using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;
using NUTrade1.Services;

namespace NUTrade1.ViewModels;

/// <summary>
/// Forgot password, code first: email → six-digit code → new password. On success the
/// student is sent back to sign in with the new password, which is the only session left —
/// completing a reset signs every other device out.
/// </summary>
[QueryProperty(nameof(PrefilledEmail), "email")]
public partial class ForgotPasswordViewModel : BaseViewModel
{
    public enum Step { Email, Code, Password }

    private const int MinPasswordLength = 8;

    private readonly IPasswordResetService _reset;
    private readonly INavigationService _nav;
    private IDispatcherTimer? _cooldownTimer;
    private string _resetToken = string.Empty;

    public ForgotPasswordViewModel(IPasswordResetService reset, INavigationService nav)
    {
        _reset = reset;
        _nav = nav;
        Title = "Reset your password";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmailStep))]
    [NotifyPropertyChangedFor(nameof(IsCodeStep))]
    [NotifyPropertyChangedFor(nameof(IsPasswordStep))]
    [NotifyPropertyChangedFor(nameof(ShowEmailForm))]
    [NotifyPropertyChangedFor(nameof(StepLabel))]
    [NotifyPropertyChangedFor(nameof(Heading))]
    [NotifyPropertyChangedFor(nameof(Subheading))]
    private Step _currentStep = Step.Email;

    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _code = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _confirmPassword = string.Empty;
    [ObservableProperty] private string _sentTo = string.Empty;

    /// <summary>Whatever was already typed on the sign-in screen, so it isn't typed twice.</summary>
    [ObservableProperty] private string? _prefilledEmail;

    partial void OnPrefilledEmailChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Email = value;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResendText))]
    [NotifyPropertyChangedFor(nameof(CanResend))]
    private int _resendInSeconds;

    public bool IsEmailStep => CurrentStep == Step.Email;

    /// <summary>The email form stays on screen behind the code modal, so the page does not jump.</summary>
    public bool ShowEmailForm => CurrentStep is Step.Email or Step.Code;
    public bool IsCodeStep => CurrentStep == Step.Code;
    public bool IsPasswordStep => CurrentStep == Step.Password;

    public string StepLabel => $"Step {(int)CurrentStep + 1} of 3";

    public string Heading => CurrentStep switch
    {
        Step.Code => "Check your email",
        Step.Password => "Choose a new password",
        _ => "Forgot your password?",
    };

    public string Subheading => CurrentStep switch
    {
        // Deliberately "if there's an account": startPasswordReset answers the same way
        // whether or not the address is registered, so the screen must not promise mail.
        Step.Code => $"If there's a NUTrade account for {SentTo}, we sent it a 6-digit code.",
        Step.Password => "Signing in again with the new password will be the only session left.",
        _ => "Enter the email you signed up with and we'll send a code to reset your password.",
    };

    public bool CanResend => ResendInSeconds <= 0;
    public string ResendText => CanResend
        ? "Send another code"
        : $"Send another code in {CountdownClock.Format(TimeSpan.FromSeconds(ResendInSeconds))}";

    public override Task OnDisappearingAsync()
    {
        StopCooldown();
        return Task.CompletedTask;
    }

    /// <summary>Step 1 → 2, and "Send another code" on step 2.</summary>
    [RelayCommand]
    private async Task SendCodeAsync()
    {
        if (IsBusy) return;
        ErrorMessage = null;

        if (!NUTradeConstants.IsValidEmail(Email))
        {
            ErrorMessage = "Enter a valid email address.";
            return;
        }
        if (IsCodeStep && !CanResend) return;

        IsBusy = true;
        try
        {
            var result = await _reset.StartAsync(Email.Trim());
            if (!result.Succeeded)
            {
                ErrorMessage = result.Error ?? "Could not send the code. Try again.";
                return;
            }

            SentTo = result.Value!.SentTo;
            Code = string.Empty;
            CurrentStep = Step.Code;
            OnPropertyChanged(nameof(Subheading));
            StartCooldown(result.Value.ResendAfterSeconds);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Step 2 → 3.</summary>
    [RelayCommand]
    private async Task VerifyCodeAsync()
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
            var result = await _reset.VerifyCodeAsync(Email.Trim(), entered);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Error ?? "That code isn't right.";
                Code = string.Empty;
                return;
            }

            _resetToken = result.Value!;
            StopCooldown();
            CurrentStep = Step.Password;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Step 3: set the new password, then go sign in with it.</summary>
    [RelayCommand]
    private async Task ResetPasswordAsync()
    {
        if (IsBusy) return;
        ErrorMessage = null;

        if (Password.Length < MinPasswordLength)
        {
            ErrorMessage = $"Use at least {MinPasswordLength} characters for your password.";
            return;
        }
        if (Password != ConfirmPassword)
        {
            ErrorMessage = "The two passwords don't match.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _reset.CompleteAsync(
                new PasswordResetDetails(Email.Trim(), _resetToken, Password));
            if (!result.Succeeded)
            {
                ErrorMessage = result.Error ?? "Could not reset your password. Try again.";
                return;
            }

            var email = Email.Trim();
            Password = ConfirmPassword = string.Empty;
            _resetToken = string.Empty;
            await _nav.GoToAsync($"//{Routes.Login}", new Dictionary<string, object> { ["resetEmail"] = email });
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task SignInAsync() => _nav.GoToAsync($"//{Routes.Login}");

    /// <summary>One step back; from the first step, back to sign in.</summary>
    [RelayCommand]
    private async Task BackAsync()
    {
        ErrorMessage = null;
        switch (CurrentStep)
        {
            case Step.Email:
                await _nav.GoBackAsync();
                break;
            case Step.Code:
                StopCooldown();
                CurrentStep = Step.Email;
                break;
            case Step.Password:
                // The code is spent once verified, so going back means starting over.
                _resetToken = string.Empty;
                CurrentStep = Step.Email;
                break;
        }
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
}

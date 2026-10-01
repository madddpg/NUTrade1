using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;
using NUTrade1.Services;

namespace NUTrade1.ViewModels;

/// <summary>
/// Create account, code first: email → six-digit code → first name, last name, program
/// and password. On success the student is sent back to sign in with the new account,
/// which is already verified — no further steps.
/// </summary>
public partial class RegisterViewModel : BaseViewModel
{
    public enum Step { Email, Code, Details }

    private const int MinPasswordLength = 8;

    private readonly IRegistrationService _registration;
    private readonly INavigationService _nav;
    private IDispatcherTimer? _cooldownTimer;
    private string _signupToken = string.Empty;

    public RegisterViewModel(IRegistrationService registration, INavigationService nav)
    {
        _registration = registration;
        _nav = nav;
        Title = "Create an account";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmailStep))]
    [NotifyPropertyChangedFor(nameof(IsCodeStep))]
    [NotifyPropertyChangedFor(nameof(IsDetailsStep))]
    [NotifyPropertyChangedFor(nameof(ShowEmailForm))]
    [NotifyPropertyChangedFor(nameof(StepLabel))]
    [NotifyPropertyChangedFor(nameof(Heading))]
    [NotifyPropertyChangedFor(nameof(Subheading))]
    private Step _currentStep = Step.Email;

    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _code = string.Empty;
    [ObservableProperty] private string _firstName = string.Empty;
    [ObservableProperty] private string _lastName = string.Empty;
    [ObservableProperty] private string? _selectedProgram;
    [ObservableProperty] private string _password = string.Empty;

    public IReadOnlyList<string> Programs => NUTradeConstants.Programs;
    [ObservableProperty] private string _confirmPassword = string.Empty;
    [ObservableProperty] private string _sentTo = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResendText))]
    [NotifyPropertyChangedFor(nameof(CanResend))]
    private int _resendInSeconds;

    public bool IsEmailStep => CurrentStep == Step.Email;

    /// <summary>The email form stays on screen behind the code modal, so the page does not jump.</summary>
    public bool ShowEmailForm => CurrentStep is Step.Email or Step.Code;
    public bool IsCodeStep => CurrentStep == Step.Code;
    public bool IsDetailsStep => CurrentStep == Step.Details;

    public string StepLabel => $"Step {(int)CurrentStep + 1} of 3";

    public string Heading => CurrentStep switch
    {
        Step.Code => "Check your email",
        Step.Details => "Almost done",
        _ => "Create your account",
    };

    public string Subheading => CurrentStep switch
    {
        Step.Code => $"We sent a 6-digit code to {SentTo}.",
        Step.Details => "Tell other students who you are, and choose a password.",
        _ => "Any email works. We'll send a code to confirm it's yours.",
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
            var result = await _registration.StartAsync(Email.Trim());
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
            var result = await _registration.VerifyCodeAsync(Email.Trim(), entered);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Error ?? "That code isn't right.";
                Code = string.Empty;
                return;
            }

            _signupToken = result.Value!;
            StopCooldown();
            CurrentStep = Step.Details;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Step 3: create the account, then go sign in with it.</summary>
    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy) return;
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName))
        {
            ErrorMessage = "Enter your first and last name.";
            return;
        }
        if (string.IsNullOrEmpty(SelectedProgram))
        {
            ErrorMessage = "Choose your program.";
            return;
        }
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
            var result = await _registration.CompleteAsync(new SignupDetails(
                Email.Trim(), _signupToken, FirstName.Trim(), LastName.Trim(), SelectedProgram, Password));
            if (!result.Succeeded)
            {
                ErrorMessage = result.Error ?? "Could not create your account. Try again.";
                return;
            }

            var email = Email.Trim();
            Password = ConfirmPassword = string.Empty;
            // Straight to Sign In, whether registration was opened from Welcome or from Login.
            await _nav.GoToAsync($"//{Routes.Login}", new Dictionary<string, object> { ["registeredEmail"] = email });
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>One step back; from the first step, back to sign in.</summary>
    [RelayCommand]
    private Task SignInAsync() => _nav.GoToAsync($"//{Routes.Login}");

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
            case Step.Details:
                // The code is spent once verified, so going back means starting over.
                _signupToken = string.Empty;
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

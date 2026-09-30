using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;
using NUTrade1.Services;

namespace NUTrade1.ViewModels;

[QueryProperty(nameof(RegisteredEmail), "registeredEmail")]
[QueryProperty(nameof(ResetEmail), "resetEmail")]
public partial class LoginViewModel : BaseViewModel
{
    private readonly IAuthService _auth;
    private readonly INavigationService _nav;

    public LoginViewModel(IAuthService auth, INavigationService nav)
    {
        _auth = auth;
        _nav = nav;
        Title = "Welcome to NUTrade";
    }

    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _password = string.Empty;

    /// <summary>"Account created" note shown after returning from registration.</summary>

    /// <summary>Handed back by <see cref="RegisterViewModel"/> once the account exists.</summary>
    [ObservableProperty] private string? _registeredEmail;

    /// <summary>Handed back by <see cref="ForgotPasswordViewModel"/> once the password is changed.</summary>
    [ObservableProperty] private string? _resetEmail;

    partial void OnRegisteredEmailChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        Email = value;
        Password = string.Empty;
        ErrorMessage = null;
        InfoMessage = "Account created. Sign in with your email and password.";
    }

    partial void OnResetEmailChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        Email = value;
        Password = string.Empty;
        ErrorMessage = null;
        InfoMessage = "Password changed. Sign in with your new password.";
    }

    /// <summary>Hides the Google button on builds with no OAuth client configured.</summary>
    public bool IsGoogleSignInAvailable => _auth.IsGoogleSignInAvailable;

    /// <summary>Back to the welcome screen (Sign Up / Sign In).</summary>
    [RelayCommand]
    private Task BackToWelcomeAsync() => _nav.GoToAsync($"//{Routes.Welcome}");

    /// <summary>Registration has its own code-first flow: email, code, then name and password.</summary>
    [RelayCommand]
    private Task CreateAccountAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;
        return _nav.GoToAsync(Routes.Register);
    }

    /// <summary>Whatever is already typed goes with them, so the email is not typed twice.</summary>
    [RelayCommand]
    private Task ForgotPasswordAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;
        return _nav.GoToAsync(
            Routes.ForgotPassword,
            new Dictionary<string, object> { ["email"] = Email.Trim() });
    }

    [RelayCommand]
    private async Task SignInWithGoogleAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _auth.SignInWithGoogleAsync();
            if (!result.Succeeded)
            {
                ErrorMessage = result.Error;
                return;
            }

            await _nav.ResetToRootAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        InfoMessage = null;
        try
        {
            var result = await _auth.SignInWithEmailAsync(Email, Password);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Error ?? "Sign-in failed. Try again.";
                return;
            }

            Password = string.Empty;
            await _nav.ResetToRootAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }
}

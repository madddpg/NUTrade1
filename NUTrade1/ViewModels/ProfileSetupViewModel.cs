using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;

namespace NUTrade1.ViewModels;

/// <summary>
/// Name setup for accounts that reach the app without a profile — Google sign-in and
/// accounts made before registration moved to the code-first flow, which collects the
/// name itself. Asks for the same thing that flow does: first and last name, and program.
/// </summary>
public partial class ProfileSetupViewModel : BaseViewModel
{
    private readonly IAuthService _auth;
    private readonly IUserService _users;
    private readonly INavigationService _nav;

    public ProfileSetupViewModel(IAuthService auth, IUserService users, INavigationService nav)
    {
        _auth = auth;
        _users = users;
        _nav = nav;
        Title = "Set up your profile";
    }

    [ObservableProperty] private string _firstName = string.Empty;
    [ObservableProperty] private string _lastName = string.Empty;
    [ObservableProperty] private string? _selectedProgram;

    public IReadOnlyList<string> Programs => NUTradeConstants.Programs;

    [RelayCommand]
    private async Task SaveAsync()
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

        IsBusy = true;
        try
        {
            var first = FirstName.Trim();
            var last = LastName.Trim();
            var profile = new UserProfile
            {
                Uid = _auth.CurrentUid ?? string.Empty,
                Email = _auth.CurrentEmail ?? string.Empty,
                FirstName = first,
                LastName = last,
                DisplayName = $"{first} {last}",
                Program = SelectedProgram,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            var result = await _users.CreateProfileAsync(profile);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Error ?? "Could not save your profile.";
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
    private async Task SignOutAsync()
    {
        await _auth.SignOutAsync();
        await _nav.ResetToRootAsync();
    }
}

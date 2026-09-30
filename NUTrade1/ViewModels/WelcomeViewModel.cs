using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;
using NUTrade1.Services;

namespace NUTrade1.ViewModels;

/// <summary>The signed-out landing screen: "Find It. Bid It. Own It." with Sign Up / Sign In.</summary>
public partial class WelcomeViewModel : BaseViewModel
{
    private readonly INavigationService _nav;

    public WelcomeViewModel(INavigationService nav)
    {
        _nav = nav;
        Title = "Welcome";
    }

    [RelayCommand]
    private Task SignUpAsync() => _nav.GoToAsync(Routes.Register);

    [RelayCommand]
    private Task SignInAsync() => _nav.GoToAsync($"//{Routes.Login}");
}

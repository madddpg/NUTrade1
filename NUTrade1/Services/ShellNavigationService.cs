using NUTrade1.Core;

namespace NUTrade1.Services;

/// <summary>Implements <see cref="INavigationService"/> over Shell routing.</summary>
public sealed class ShellNavigationService : INavigationService
{
    private readonly IAuthService _auth;
    private readonly IUserService _users;

    public ShellNavigationService(IAuthService auth, IUserService users)
    {
        _auth = auth;
        _users = users;
    }

    public Task GoToAsync(string route, IDictionary<string, object>? parameters = null) =>
        MainThread.InvokeOnMainThreadAsync(() =>
            parameters is null
                ? Shell.Current.GoToAsync(route)
                : Shell.Current.GoToAsync(route, parameters));

    public Task GoBackAsync() =>
        MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(".."));

    public async Task ResetToRootAsync()
    {
        string target;
        if (!_auth.IsSignedIn)
        {
            target = $"//{Routes.Welcome}";
        }
        else
        {
            var profile = await _users.GetCurrentProfileAsync();
            target = profile is null ? $"//{Routes.ProfileSetup}" : Routes.FeedTab;
        }

        await MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(target));
    }
}

namespace NUTrade1.Core;

/// <summary>
/// A thin navigation seam so ViewModels stay free of MAUI types. The app layer
/// implements this over Shell routing.
/// </summary>
public interface INavigationService
{
    Task GoToAsync(string route, IDictionary<string, object>? parameters = null);

    Task GoBackAsync();

    /// <summary>Navigates to the correct root for the current auth / profile state.</summary>
    Task ResetToRootAsync();
}

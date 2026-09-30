using NUTrade1.Services.Firebase;
using NUTrade1.ViewModels;

namespace NUTrade1.Views;

/// <summary>
/// ContentPage that relays MAUI's appearing / disappearing lifecycle to its
/// <see cref="BaseViewModel"/> so ViewModels can load data and detach listeners.
/// </summary>
public abstract class AppContentPage : ContentPage
{
    // Both handlers are async void, so anything a ViewModel throws out of them is an
    // unhandled exception and Android kills the app on the spot. A denied read or a
    // dropped connection on one tab is not worth that: it becomes a toast instead.

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is not BaseViewModel vm) return;
        try
        {
            await vm.OnAppearingAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            vm.ErrorMessage = ex is FirestoreException
                ? ex.Message
                : "Couldn't load this screen. Check your connection and try again.";
        }
        catch (OperationCanceledException)
        {
        }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is not BaseViewModel vm) return;
        try
        {
            await vm.OnDisappearingAsync();
        }
        catch (Exception)
        {
            // Detaching listeners on the way out — nothing on screen to report it to.
        }
    }
}

using NUTrade1.Services;

namespace NUTrade1.Controls;

/// <summary>
/// Draws <see cref="NotificationCenter"/>'s active notification at the top of its page.
/// Every page carries one; only the page on screen shows the card, so a notification
/// follows the student from screen to screen until they act on it or dismiss it.
/// </summary>
public partial class NotificationHost : ContentView
{
    private const double HiddenOffset = -180;

    private Page? _page;
    private bool _pageVisible;
    private AppNotification? _shown;

    public NotificationHost()
    {
        InitializeComponent();
        Card.TranslationY = HiddenOffset;
        Card.Opacity = 0;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        NotificationCenter.Current.Changed += OnCenterChanged;

        _page = FindPage();
        if (_page is not null)
        {
            _page.Appearing += OnPageAppearing;
            _page.Disappearing += OnPageDisappearing;
        }
        _pageVisible = true;
        _ = SyncAsync();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        NotificationCenter.Current.Changed -= OnCenterChanged;
        if (_page is not null)
        {
            _page.Appearing -= OnPageAppearing;
            _page.Disappearing -= OnPageDisappearing;
        }
    }

    private void OnPageAppearing(object? sender, EventArgs e)
    {
        _pageVisible = true;
        _ = SyncAsync();
    }

    private void OnPageDisappearing(object? sender, EventArgs e)
    {
        _pageVisible = false;
        _shown = null;
        IsVisible = false;
        Card.TranslationY = HiddenOffset;
        Card.Opacity = 0;
    }

    private void OnCenterChanged(object? sender, EventArgs e) => _ = SyncAsync();

    /// <summary>Brings the card in line with the centre: show, swap or hide.</summary>
    private async Task SyncAsync()
    {
        var active = _pageVisible ? NotificationCenter.Current.Active : null;
        if (ReferenceEquals(active, _shown)) return;

        if (_shown is not null) await HideAsync();

        _shown = active;
        if (active is null) return;

        TitleLabel.Text = active.Title;
        BodyLabel.Text = active.Body;
        ActionLabel.Text = active.ActionText;
        ActionButton.IsVisible = !string.IsNullOrEmpty(active.ActionText);
        SemanticScreenReader.Announce($"{active.Title}. {active.Body}");

        IsVisible = true;
        await Task.WhenAll(
            Card.TranslateToAsync(0, 0, 320, Easing.CubicOut),
            Card.FadeToAsync(1, 200));
    }

    private async Task HideAsync()
    {
        await Task.WhenAll(
            Card.TranslateToAsync(0, HiddenOffset, 220, Easing.CubicIn),
            Card.FadeToAsync(0, 220));
        IsVisible = false;
    }

    private void OnDismissTapped(object? sender, TappedEventArgs e)
    {
        if (_shown is { } n) NotificationCenter.Current.Dismiss(n);
    }

    private async void OnActionTapped(object? sender, TappedEventArgs e)
    {
        if (_shown is not { } n) return;
        NotificationCenter.Current.Dismiss(n);
        if (n.Action is not null) await n.Action();
    }

    private Page? FindPage()
    {
        Element? e = Parent;
        while (e is not null and not Page) e = e.Parent;
        return e as Page;
    }
}

using Microsoft.Maui.Controls.Shapes;
using NUTrade1.Services;

namespace NUTrade1.Controls;

/// <summary>
/// Draws <see cref="Toaster"/>'s active message as a pill at the top-centre of its page:
/// the one place in the app where a success, a failure or an error shows up.
///
/// Every page carries one, the same arrangement <see cref="NotificationHost"/> uses —
/// only the page on screen draws anything, so a message raised during navigation lands on
/// whichever page the student ends up on.
/// </summary>
public partial class ToastHost : ContentView
{
    private const double HiddenOffset = -140;

    /// <summary>
    /// Clears the status bar and any notch. MAUI does not expose per-device insets to
    /// shared code, so this is the conservative constant the design already uses for the
    /// notification card, plus a little — a toast that tucks under a camera cutout is
    /// worse than one sitting slightly low.
    /// </summary>
    private static readonly Thickness TopInset = new(0, 14, 0, 0);

    private Page? _page;
    private bool _pageVisible;
    private Toast? _shown;

    public ToastHost()
    {
        InitializeComponent();
        Margin = TopInset;
        Pill.TranslationY = HiddenOffset;
        Pill.Opacity = 0;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        Toaster.Current.Changed += OnToasterChanged;

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
        Toaster.Current.Changed -= OnToasterChanged;
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
        // Snap rather than animate: this page is on its way off screen, and the host on
        // the incoming page picks the same toast up from the Toaster.
        _pageVisible = false;
        _shown = null;
        IsVisible = false;
        Pill.TranslationY = HiddenOffset;
        Pill.Opacity = 0;
    }

    private void OnToasterChanged(object? sender, EventArgs e) => _ = SyncAsync();

    private async Task SyncAsync()
    {
        var active = _pageVisible ? Toaster.Current.Active : null;
        if (ReferenceEquals(active, _shown)) return;

        if (_shown is not null) await HideAsync();

        _shown = active;
        if (active is null) return;

        Apply(active);

        // Screen readers get the message even though it is a purely visual overlay; an
        // assertive announcement interrupts, which is right for a failure.
        SemanticScreenReader.Announce(active.Message);

        IsVisible = true;
        await Task.WhenAll(
            Pill.TranslateToAsync(0, 0, 260, Easing.CubicOut),
            Pill.FadeToAsync(1, 160));

        // An animation that is interrupted — by a second toast, or by the page going away
        // mid-flight — can leave the pill a fraction short of opaque, which reads as a
        // blurred, half-washed message rather than as a fade.
        Pill.Opacity = 1;
        Pill.TranslationY = 0;
    }

    /// <summary>
    /// Solid fills rather than tinted backgrounds: at a glance, from arm's length, colour
    /// is the whole signal, and the icon carries it for anyone who cannot use the colour.
    /// </summary>
    private void Apply(Toast toast)
    {
        var (colourKey, glyphKey) = toast.Kind switch
        {
            ToastKind.Success => ("Success", "IconCheckCircle"),
            ToastKind.Error => ("Danger", "IconAlertCircle"),
            _ => ("NuNavy", "IconInfoCircle"),
        };

        Pill.BackgroundColor = Resource<Color>(colourKey) ?? Colors.DimGray;
        if (Resource<Style>(glyphKey) is { } style) Glyph.Style = style;
        Glyph.Fill = Resource<Color>("White") is { } white ? new SolidColorBrush(white) : Brush.White;
        MessageLabel.Text = toast.Message;
    }

    private async Task HideAsync()
    {
        await Task.WhenAll(
            Pill.TranslateToAsync(0, HiddenOffset, 180, Easing.CubicIn),
            Pill.FadeToAsync(0, 180));
        IsVisible = false;
    }

    private void OnDismissRequested(object? sender, EventArgs e)
    {
        if (_shown is { } toast) Toaster.Current.Dismiss(toast);
    }

    private static T? Resource<T>(string key) where T : class =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true ? value as T : null;

    private Page? FindPage()
    {
        Element? element = Parent;
        while (element is not null and not Page) element = element.Parent;
        return element as Page;
    }
}

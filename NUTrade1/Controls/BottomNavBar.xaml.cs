using NUTrade1.Services;

namespace NUTrade1.Controls;

/// <summary>
/// The floating bottom navigation from the mockups: five icon-only tabs. Drop it as the
/// last child of a page-root <see cref="Grid"/> and set <see cref="Active"/> to the
/// current tab.
/// </summary>
public partial class BottomNavBar : ContentView
{
    public BottomNavBar()
    {
        InitializeComponent();
        Refresh();
    }

    public static readonly BindableProperty ActiveProperty = BindableProperty.Create(
        nameof(Active), typeof(string), typeof(BottomNavBar), "Home",
        propertyChanged: (b, _, _) => ((BottomNavBar)b).Refresh());

    /// <summary>One of "Home", "Offers", "Post", "Chat", "Profile".</summary>
    public string Active
    {
        get => (string)GetValue(ActiveProperty);
        set => SetValue(ActiveProperty, value);
    }

    public static readonly BindableProperty OffersBadgeProperty = BindableProperty.Create(
        nameof(OffersBadge), typeof(int), typeof(BottomNavBar), 0,
        propertyChanged: (b, _, _) => ((BottomNavBar)b).Refresh());

    public int OffersBadge
    {
        get => (int)GetValue(OffersBadgeProperty);
        set => SetValue(OffersBadgeProperty, value);
    }

    public static readonly BindableProperty ChatBadgeProperty = BindableProperty.Create(
        nameof(ChatBadge), typeof(int), typeof(BottomNavBar), 0,
        propertyChanged: (b, _, _) => ((BottomNavBar)b).Refresh());

    public int ChatBadge
    {
        get => (int)GetValue(ChatBadgeProperty);
        set => SetValue(ChatBadgeProperty, value);
    }

    private void Refresh()
    {
        ApplyState(Active == "Home", HomePill, HomeIcon);
        ApplyState(Active == "Offers", OffersPill, OffersIcon);
        ApplyState(Active == "Chat", ChatPill, ChatIcon);
        ApplyState(Active == "Profile", ProfilePill, ProfileIcon);

        // "+" is dark ink when idle and a gold circle while you are posting.
        var posting = Active == "Post";
        PostPill.BackgroundColor = posting ? AppColor("NuGold", "NuGold") : Colors.Transparent;
        PostIcon.Stroke = posting ? AppColor("NuNavy", "NuNavy") : AppColor("Ink", "InkDark");

        SetBadge(OffersBadgeView, OffersBadgeLabel, OffersBadge);
        SetBadge(ChatBadgeView, ChatBadgeLabel, ChatBadge);
    }

    private static void ApplyState(bool active, Border pill, Microsoft.Maui.Controls.Shapes.Path icon)
    {
        pill.BackgroundColor = active ? AppColor("NuNavy", "NuNavy") : Colors.Transparent;
        icon.Stroke = active ? AppColor("White", "White") : AppColor("InkMuted", "InkMutedDark");
    }

    private static void SetBadge(Border view, Label label, int count)
    {
        view.IsVisible = count > 0;
        label.Text = count > 9 ? "9+" : count.ToString();
    }

    private static Color AppColor(string lightKey, string darkKey)
    {
        var key = Application.Current?.RequestedTheme == AppTheme.Dark ? darkKey : lightKey;
        return Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c
            ? c
            : Colors.Gray;
    }

    private static Task Go(string route) =>
        Shell.Current?.GoToAsync(route) ?? Task.CompletedTask;

    private void OnHomeTapped(object? sender, TappedEventArgs e) => _ = Go(Routes.FeedTab);
    private void OnOffersTapped(object? sender, TappedEventArgs e) => _ = Go(Routes.OffersTab);
    private void OnChatTapped(object? sender, TappedEventArgs e) => _ = Go(Routes.ChatsTab);
    private void OnProfileTapped(object? sender, TappedEventArgs e) => _ = Go(Routes.ProfileTab);
    private void OnPostTapped(object? sender, TappedEventArgs e) => _ = Go(Routes.PostTradeTab);
}

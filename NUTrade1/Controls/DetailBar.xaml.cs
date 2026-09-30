namespace NUTrade1.Controls;

/// <summary>A back-arrow header for pushed (non-tab) pages, since the Shell nav bar is hidden.</summary>
public partial class DetailBar : ContentView
{
    public DetailBar()
    {
        InitializeComponent();
    }

    public static readonly BindableProperty HeaderTitleProperty = BindableProperty.Create(
        nameof(HeaderTitle), typeof(string), typeof(DetailBar), "Back",
        propertyChanged: (b, _, v) => ((DetailBar)b).TitleLabel.Text = (string?)v ?? "Back");

    public string HeaderTitle
    {
        get => (string)GetValue(HeaderTitleProperty);
        set => SetValue(HeaderTitleProperty, value);
    }

    private void OnBackTapped(object? sender, EventArgs e) =>
        _ = Shell.Current?.GoToAsync("..");
}

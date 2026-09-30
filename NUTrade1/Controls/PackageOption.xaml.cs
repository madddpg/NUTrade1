using System.Windows.Input;

namespace NUTrade1.Controls;

/// <summary>
/// A selectable package row for the Post screen ("Free · ₱0"). Works like
/// <see cref="SelectableChip"/>: bind <see cref="IsSelected"/> to the group's ViewModel
/// property through a converter, and handle the write through <see cref="Command"/>,
/// which fires with <see cref="Value"/> as its parameter.
/// </summary>
public partial class PackageOption : ContentView
{
    public PackageOption()
    {
        InitializeComponent();
        Repaint();
    }

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(PackageOption), string.Empty,
        propertyChanged: (b, _, v) =>
        {
            var self = (PackageOption)b;
            self.TitleLabel.Text = v as string;
            self.Repaint();
        });

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly BindableProperty SubtitleProperty = BindableProperty.Create(
        nameof(Subtitle), typeof(string), typeof(PackageOption), string.Empty,
        propertyChanged: (b, _, v) => ((PackageOption)b).SubtitleLabel.Text = v as string);

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public static readonly BindableProperty PriceProperty = BindableProperty.Create(
        nameof(Price), typeof(string), typeof(PackageOption), string.Empty,
        propertyChanged: (b, _, v) =>
        {
            var self = (PackageOption)b;
            self.PriceLabel.Text = v as string;
            self.Repaint();
        });

    public string Price
    {
        get => (string)GetValue(PriceProperty);
        set => SetValue(PriceProperty, value);
    }

    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value), typeof(string), typeof(PackageOption), string.Empty);

    /// <summary>The package this row represents; passed to <see cref="Command"/> on tap.</summary>
    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly BindableProperty IsSelectedProperty = BindableProperty.Create(
        nameof(IsSelected), typeof(bool), typeof(PackageOption), false,
        propertyChanged: (b, _, _) => ((PackageOption)b).Repaint());

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(PackageOption));

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (Command?.CanExecute(Value) == true)
            Command.Execute(Value);
    }

    private void Repaint()
    {
        // The mockup's selected row: a gold outline on the same white fill.
        Root.Stroke = IsSelected ? Res("NuGold", "NuGold") : Res("CardBorder", "CardBorderDark");
        Root.StrokeThickness = IsSelected ? 1.6 : 1;
        Root.BackgroundColor = Res("White", "CardBgDark");

        SemanticProperties.SetDescription(Root,
            $"{Title} package, {Price}{(IsSelected ? ", selected" : string.Empty)}");
    }

    private static Color Res(string lightKey, string darkKey)
    {
        var key = Application.Current?.RequestedTheme == AppTheme.Dark ? darkKey : lightKey;
        return Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c
            ? c
            : Colors.Gray;
    }
}

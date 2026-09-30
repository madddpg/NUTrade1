using System.Windows.Input;

namespace NUTrade1.Controls;

/// <summary>
/// A pill-shaped filter/selection chip that actually responds to taps (unlike a
/// templated <see cref="RadioButton"/>). Bind <see cref="IsSelected"/> to the
/// group's ViewModel property via a converter and handle the write through
/// <see cref="Command"/>, which fires with <see cref="Value"/> as its parameter.
/// </summary>
public partial class SelectableChip : ContentView
{
    public SelectableChip()
    {
        InitializeComponent();
        Repaint();
    }

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(SelectableChip), string.Empty,
        propertyChanged: (b, _, v) => ((SelectableChip)b).ChipLabel.Text = v as string ?? string.Empty);

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value), typeof(string), typeof(SelectableChip), string.Empty);

    /// <summary>The value this chip represents; passed to <see cref="Command"/> on tap.</summary>
    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly BindableProperty IsSelectedProperty = BindableProperty.Create(
        nameof(IsSelected), typeof(bool), typeof(SelectableChip), false,
        propertyChanged: (b, _, _) => ((SelectableChip)b).Repaint());

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(SelectableChip));

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public static readonly BindableProperty BlockProperty = BindableProperty.Create(
        nameof(Block), typeof(bool), typeof(SelectableChip), false,
        propertyChanged: (b, _, _) => ((SelectableChip)b).ApplyBlock());

    /// <summary>Full-width variant used for the two-up "Swap preference" choice.</summary>
    public bool Block
    {
        get => (bool)GetValue(BlockProperty);
        set => SetValue(BlockProperty, value);
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (Command?.CanExecute(Value) == true)
            Command.Execute(Value);
    }

    private void ApplyBlock()
    {
        if (Block)
        {
            Root.Padding = new Thickness(16, 14);
            Root.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(18) };
            Root.HorizontalOptions = LayoutOptions.Fill;
            ChipLabel.FontSize = 14;
        }
    }

    private void Repaint()
    {
        var navy = Res("NuNavy", "NuNavyLight");
        var border = Res("ChipBorder", "ChipBorderDark");
        var chipBg = Res("ChipBg", "ChipBgDark");
        var chipText = Res("ChipText", "ChipTextDark");
        var onNavy = Res("White", "NuNavy");

        Root.BackgroundColor = IsSelected ? navy : chipBg;
        Root.Stroke = IsSelected ? navy : border;
        ChipLabel.TextColor = IsSelected ? onNavy : chipText;
    }

    private static Color Res(string lightKey, string darkKey)
    {
        var key = Application.Current?.RequestedTheme == AppTheme.Dark ? darkKey : lightKey;
        return Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c
            ? c
            : Colors.Gray;
    }
}

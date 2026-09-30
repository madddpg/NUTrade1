using System.Windows.Input;

namespace NUTrade1.Controls;

/// <summary>
/// A password <see cref="Entry"/> with the show/hide eye every sign-in form has, so a
/// student mistyping a password on a phone keyboard can check it rather than guess.
///
/// Drop-in for the <c>InputShell</c> + <c>ShellEntry</c> pair it replaces:
/// <c>&lt;controls:PasswordField Text="{Binding Password}" Placeholder="Password" /&gt;</c>.
/// <see cref="Text"/> is two-way by default; the visibility state is the control's own
/// and never reaches the ViewModel.
/// </summary>
public partial class PasswordField : ContentView
{
    public PasswordField() => InitializeComponent();

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(PasswordField), string.Empty,
        defaultBindingMode: BindingMode.TwoWay);

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder), typeof(string), typeof(PasswordField), string.Empty);

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public static readonly BindableProperty ReturnTypeProperty = BindableProperty.Create(
        nameof(ReturnType), typeof(ReturnType), typeof(PasswordField), Microsoft.Maui.ReturnType.Default);

    /// <summary>Mirrors <see cref="Entry.ReturnType"/> so a form can keep its Next / Go chain.</summary>
    public ReturnType ReturnType
    {
        get => (ReturnType)GetValue(ReturnTypeProperty);
        set => SetValue(ReturnTypeProperty, value);
    }

    public static readonly BindableProperty ReturnCommandProperty = BindableProperty.Create(
        nameof(ReturnCommand), typeof(ICommand), typeof(PasswordField));

    public ICommand? ReturnCommand
    {
        get => (ICommand?)GetValue(ReturnCommandProperty);
        set => SetValue(ReturnCommandProperty, value);
    }

    public static readonly BindableProperty IsHiddenProperty = BindableProperty.Create(
        nameof(IsHidden), typeof(bool), typeof(PasswordField), true,
        propertyChanged: (b, _, v) => ((PasswordField)b).ApplyGlyphs((bool)v));

    /// <summary>True while the characters are masked. Starts masked, as a password field must.</summary>
    public bool IsHidden
    {
        get => (bool)GetValue(IsHiddenProperty);
        set => SetValue(IsHiddenProperty, value);
    }

    private void OnToggleTapped(object? sender, TappedEventArgs e) => IsHidden = !IsHidden;

    /// <summary>The open eye offers "show"; the struck-through eye offers "hide".</summary>
    private void ApplyGlyphs(bool hidden)
    {
        ShowGlyph.IsVisible = hidden;
        HideGlyph.IsVisible = !hidden;
    }
}

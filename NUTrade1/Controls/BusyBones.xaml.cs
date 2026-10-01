namespace NUTrade1.Controls;

/// <summary>
/// The waiting state for an action already on screen: a caption and a few bones,
/// in place of a spinner.
/// </summary>
public partial class BusyBones : ContentView
{
    public BusyBones()
    {
        InitializeComponent();
    }

    public static readonly BindableProperty IsActiveProperty = BindableProperty.Create(
        nameof(IsActive), typeof(bool), typeof(BusyBones), false);

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public static readonly BindableProperty CaptionProperty = BindableProperty.Create(
        nameof(Caption), typeof(string), typeof(BusyBones), string.Empty,
        propertyChanged: (b, _, _) => ((BusyBones)b).OnPropertyChanged(nameof(HasCaption)));

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public bool HasCaption => !string.IsNullOrWhiteSpace(Caption);
}

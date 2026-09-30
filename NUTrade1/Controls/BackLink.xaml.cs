namespace NUTrade1.Controls;

/// <summary>"‹ Back" link for inner screens; pops the current Shell page.</summary>
public partial class BackLink : ContentView
{
    public BackLink()
    {
        InitializeComponent();
    }

    private void OnTapped(object? sender, TappedEventArgs e) =>
        _ = Shell.Current?.GoToAsync("..");
}

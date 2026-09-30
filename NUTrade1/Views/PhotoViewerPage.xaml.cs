using NUTrade1.Controls;
using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class PhotoViewerPage : AppContentPage
{
    private static readonly bool ShowArrows = DeviceInfo.Idiom == DeviceIdiom.Desktop;

    private readonly PhotoViewerViewModel _vm;
    private bool _positioned;

    public PhotoViewerPage(PhotoViewerViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_positioned) return;
        _positioned = true;

        // The viewer opens at 0 and then moves to the tapped photo: a CarouselView only
        // scrolls on a real change of Position, and ignores its first value while its
        // items are still being created. (On WinUI an *animated* change also snapped
        // back to 0 — hence IsScrollAnimated="False" in the XAML.)
        var start = _vm.StartIndex;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), () =>
        {
            if (start > 0 && start < _vm.Photos.Count) _vm.Position = start;
            UpdateArrows();
        });
    }

    private void OnPositionChanged(object? sender, PositionChangedEventArgs e)
    {
        // Leaving a zoomed photo behind would leave swiping switched off.
        _vm.IsZoomed = false;
        UpdateArrows();
    }

    /// <summary>Zooming locks the carousel so a drag moves around the photo instead of changing it.</summary>
    private void OnZoomChanged(object? sender, bool zoomed)
    {
        if (sender is ZoomableImage image && image.BindingContext as string != _vm.Photos.ElementAtOrDefault(_vm.Position))
            return; // A recycled view off screen resetting itself.

        _vm.IsZoomed = zoomed;
        UpdateArrows();
    }

    private void UpdateArrows()
    {
        var many = _vm.Photos.Count > 1 && ShowArrows && !_vm.IsZoomed;
        PreviousButton.IsVisible = many && _vm.Position > 0;
        NextButton.IsVisible = many && _vm.Position < _vm.Photos.Count - 1;
    }
}

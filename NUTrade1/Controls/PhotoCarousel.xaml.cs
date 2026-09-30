using System.Collections;
using System.Windows.Input;

namespace NUTrade1.Controls;

/// <summary>
/// A swipeable strip of a listing's photos with a "2 / 4" counter and page dots. Tapping a
/// photo runs <see cref="PhotoTappedCommand"/> with that photo's index, which screens use to
/// open the full-screen viewer at the same picture.
/// </summary>
public partial class PhotoCarousel : ContentView
{
    public static readonly BindableProperty PhotosProperty = BindableProperty.Create(
        nameof(Photos), typeof(IEnumerable), typeof(PhotoCarousel), propertyChanged: OnPhotosChanged);

    public static readonly BindableProperty PhotoTappedCommandProperty = BindableProperty.Create(
        nameof(PhotoTappedCommand), typeof(ICommand), typeof(PhotoCarousel));

    /// <summary>A mouse can't swipe a carousel, so desktops get arrows as well.</summary>
    private static readonly bool ShowArrows = DeviceInfo.Idiom == DeviceIdiom.Desktop;

    private int _count;

    public PhotoCarousel()
    {
        InitializeComponent();
    }

    /// <summary>Image sources — download URLs for a posted listing, local files on the review screen.</summary>
    public IEnumerable? Photos
    {
        get => (IEnumerable?)GetValue(PhotosProperty);
        set => SetValue(PhotosProperty, value);
    }

    /// <summary>Runs with the tapped photo's index (an <see cref="int"/>).</summary>
    public ICommand? PhotoTappedCommand
    {
        get => (ICommand?)GetValue(PhotoTappedCommandProperty);
        set => SetValue(PhotoTappedCommandProperty, value);
    }

    private static void OnPhotosChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var carousel = (PhotoCarousel)bindable;
        // A snapshot, so a later change to the source list can't leave the counter wrong.
        var photos = (newValue as IEnumerable)?.Cast<object>().ToList() ?? [];

        // A screen reloading the same listing (coming back from the viewer, say) hands
        // over a new list with the same photos; keep the student's place in it.
        if (carousel.Carousel.ItemsSource is List<object> shown && shown.SequenceEqual(photos)) return;

        carousel._count = photos.Count;
        carousel.Carousel.ItemsSource = photos;
        carousel.Refresh(0);
    }

    private void OnPositionChanged(object? sender, PositionChangedEventArgs e) => Refresh(e.CurrentPosition);

    private void Refresh(int position)
    {
        var many = _count > 1;
        EmptyIcon.IsVisible = _count == 0;
        Counter.IsVisible = many;
        Indicator.IsVisible = many;
        CounterLabel.Text = $"{Math.Clamp(position + 1, 1, Math.Max(_count, 1))} / {_count}";
        PreviousButton.IsVisible = ShowArrows && many && position > 0;
        NextButton.IsVisible = ShowArrows && many && position < _count - 1;
    }

    private void OnPhotoTapped(object? sender, TappedEventArgs e)
    {
        var index = Carousel.Position;
        if (PhotoTappedCommand?.CanExecute(index) == true) PhotoTappedCommand.Execute(index);
    }

    private void OnPreviousTapped(object? sender, TappedEventArgs e) => MoveBy(-1);

    private void OnNextTapped(object? sender, TappedEventArgs e) => MoveBy(1);

    private void MoveBy(int delta)
    {
        var target = Math.Clamp(Carousel.Position + delta, 0, Math.Max(_count - 1, 0));
        if (target != Carousel.Position) Carousel.Position = target;
    }
}

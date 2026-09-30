using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;

namespace NUTrade1.ViewModels;

/// <summary>
/// Full-screen photo viewer. Takes the photo list and the index to open at as navigation
/// parameters (passed as objects, not query strings, so the URLs arrive intact).
/// </summary>
public partial class PhotoViewerViewModel : BaseViewModel, IQueryAttributable
{
    public const string PhotosKey = "photos";
    public const string IndexKey = "index";
    public const string TitleKey = "title";

    private readonly INavigationService _nav;

    public PhotoViewerViewModel(INavigationService nav)
    {
        _nav = nav;
    }

    public ObservableCollection<string> Photos { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CounterText))]
    private int _position;

    /// <summary>True while the photo on screen is zoomed; swiping to the next one is off meanwhile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSwipeEnabled))]
    private bool _isZoomed;

    /// <summary>The index the viewer opened at, applied once the carousel has its items.</summary>
    public int StartIndex { get; private set; }

    public bool IsSwipeEnabled => !IsZoomed;

    public bool HasMany => Photos.Count > 1;

    public string CounterText => Photos.Count == 0 ? string.Empty : $"{Position + 1} / {Photos.Count}";

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Photos.Clear();
        if (query.TryGetValue(PhotosKey, out var value) && value is IEnumerable<string> photos)
            foreach (var photo in photos.Where(p => !string.IsNullOrWhiteSpace(p)))
                Photos.Add(photo);

        Title = query.TryGetValue(TitleKey, out var title) ? title as string ?? "Photos" : "Photos";
        StartIndex = query.TryGetValue(IndexKey, out var index) && index is int i
            ? Math.Clamp(i, 0, Math.Max(Photos.Count - 1, 0))
            : 0;
        // Start at 0: the page moves to StartIndex once the carousel is drawn, because a
        // CarouselView only scrolls on a real change of Position, not on its first value.
        Position = 0;

        OnPropertyChanged(nameof(HasMany));
        OnPropertyChanged(nameof(CounterText));
    }

    [RelayCommand]
    private Task CloseAsync() => _nav.GoBackAsync();

    [RelayCommand]
    private void Previous()
    {
        if (!IsZoomed && Position > 0) Position--;
    }

    [RelayCommand]
    private void Next()
    {
        if (!IsZoomed && Position < Photos.Count - 1) Position++;
    }
}

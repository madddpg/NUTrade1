namespace NUTrade1.Controls;

/// <summary>
/// An image that can be pinched (or double-tapped) up to 4x and dragged around while
/// zoomed, for reading the small print on a listing photo — a textbook's edition, a
/// uniform's size tag.
///
/// The drag gesture is only attached while zoomed. A pan recognizer that is always there
/// swallows the horizontal swipe the surrounding carousel needs to change photo.
/// </summary>
public sealed class ZoomableImage : ContentView
{
    private const double MaxScale = 4;
    private const double DoubleTapScale = 2.5;

    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source), typeof(ImageSource), typeof(ZoomableImage),
        propertyChanged: (b, _, n) => ((ZoomableImage)b).OnSourceChanged((ImageSource?)n));

    private readonly Image _image = new() { Aspect = Aspect.AspectFit, AnchorX = 0, AnchorY = 0 };
    private readonly PanGestureRecognizer _pan = new();
    private double _pinchStartScale = 1;
    private double _panStartX;
    private double _panStartY;

    /// <summary>Raised with true when the image zooms in from 1x, and false when it returns to 1x.</summary>
    public event EventHandler<bool>? ZoomChanged;

    public ZoomableImage()
    {
        Content = _image;
        IsClippedToBounds = true;

        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnPinchUpdated;
        GestureRecognizers.Add(pinch);

        var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        doubleTap.Tapped += OnDoubleTapped;
        GestureRecognizers.Add(doubleTap);

        _pan.PanUpdated += OnPanUpdated;
    }

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public bool IsZoomed => _image.Scale > 1.01;

    /// <summary>Back to 1x — used when the carousel recycles this view for another photo.</summary>
    public void ResetZoom(bool animate = false)
    {
        if (animate)
        {
            _ = _image.ScaleToAsync(1, 180, Easing.CubicOut);
            _ = _image.TranslateToAsync(0, 0, 180, Easing.CubicOut);
        }
        else
        {
            _image.Scale = 1;
            _image.TranslationX = 0;
            _image.TranslationY = 0;
        }
        SetZoomed(false);
    }

    private void OnSourceChanged(ImageSource? source)
    {
        _image.Source = source;
        ResetZoom();
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started:
                _pinchStartScale = _image.Scale;
                break;

            case GestureStatus.Running:
            {
                var oldScale = _image.Scale;
                var newScale = Math.Clamp(oldScale + (e.Scale - 1) * _pinchStartScale, 1, MaxScale);

                // Keep the point under the fingers where it is: with the anchor at the
                // top-left, that point sits at translation + origin * size * scale.
                var originX = e.ScaleOrigin.X * Width;
                var originY = e.ScaleOrigin.Y * Height;
                var contentX = (originX - _image.TranslationX) / oldScale;
                var contentY = (originY - _image.TranslationY) / oldScale;

                _image.Scale = newScale;
                MoveTo(originX - contentX * newScale, originY - contentY * newScale);
                break;
            }

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (_image.Scale <= 1.01) ResetZoom(animate: true);
                else SetZoomed(true);
                break;
        }
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartX = _image.TranslationX;
                _panStartY = _image.TranslationY;
                break;
            case GestureStatus.Running:
                MoveTo(_panStartX + e.TotalX, _panStartY + e.TotalY);
                break;
        }
    }

    /// <summary>Zooms into the middle, or back out — the only zoom a mouse user has.</summary>
    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (IsZoomed)
        {
            ResetZoom(animate: true);
            return;
        }

        _image.Scale = DoubleTapScale;
        MoveTo(-Width * (DoubleTapScale - 1) / 2, -Height * (DoubleTapScale - 1) / 2);
        SetZoomed(true);
    }

    /// <summary>Moves the zoomed image, never so far that an empty edge shows.</summary>
    private void MoveTo(double x, double y)
    {
        _image.TranslationX = Math.Clamp(x, -Width * (_image.Scale - 1), 0);
        _image.TranslationY = Math.Clamp(y, -Height * (_image.Scale - 1), 0);
    }

    private bool _zoomed;

    private void SetZoomed(bool zoomed)
    {
        if (zoomed == _zoomed) return;
        _zoomed = zoomed;

        if (zoomed) GestureRecognizers.Add(_pan);
        else GestureRecognizers.Remove(_pan);

        ZoomChanged?.Invoke(this, zoomed);
    }
}

namespace NUTrade1.Controls;

/// <summary>
/// The app's loading indicator: three dots rising and fading in sequence, NU navy either
/// side of gold, with an optional caption underneath.
///
/// Replaces the platform <see cref="ActivityIndicator"/>, which drew an unbranded grey
/// arc that was easy to miss and told the student nothing about what was happening. Bind
/// <see cref="IsRunning"/> the way you would an ActivityIndicator — the control hides
/// itself when it is not running, so there is no separate IsVisible to keep in step.
/// </summary>
public partial class LoadingDots : ContentView
{
    private const uint CycleMs = 1000;

    /// <summary>Each dot starts a third of a cycle after the one before it.</summary>
    private static readonly uint[] DotDelays = [0, CycleMs / 3, CycleMs * 2 / 3];

    private bool _animating;

    public LoadingDots()
    {
        InitializeComponent();
        Unloaded += (_, _) => StopAnimation();
    }

    public static readonly BindableProperty IsRunningProperty = BindableProperty.Create(
        nameof(IsRunning), typeof(bool), typeof(LoadingDots), false,
        propertyChanged: (b, _, v) => ((LoadingDots)b).OnIsRunningChanged((bool)v));

    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    public static readonly BindableProperty CaptionProperty = BindableProperty.Create(
        nameof(Caption), typeof(string), typeof(LoadingDots), string.Empty,
        propertyChanged: (b, _, _) => ((LoadingDots)b).OnPropertyChanged(nameof(HasCaption)));

    /// <summary>Optional line under the dots, e.g. "Sending your code…".</summary>
    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public bool HasCaption => !string.IsNullOrWhiteSpace(Caption);

    private void OnIsRunningChanged(bool running)
    {
        IsVisible = running;
        if (running) StartAnimation();
        else StopAnimation();
    }

    private void StartAnimation()
    {
        if (_animating) return;
        _animating = true;

        var dots = new View[] { Dot1, Dot2, Dot3 };
        for (var i = 0; i < dots.Length; i++) Pulse(dots[i], DotDelays[i]);
    }

    /// <summary>
    /// One dot's loop, as a repeating <see cref="Animation"/> rather than an async
    /// await-loop: the platform animation driver owns the timing, so it pauses with the
    /// app instead of spinning a task in the background.
    /// </summary>
    private void Pulse(View dot, uint delayMs)
    {
        var animation = new Animation();

        // Up and back down over the first two thirds, then rest — the pause is what makes
        // the three dots read as a wave rather than a jitter.
        animation.Add(0.00, 0.33, new Animation(v => dot.TranslationY = -5 * v, 0, 1, Easing.CubicOut));
        animation.Add(0.33, 0.66, new Animation(v => dot.TranslationY = -5 * (1 - v), 0, 1, Easing.CubicIn));
        animation.Add(0.00, 0.33, new Animation(v => dot.Opacity = 0.45 + (0.55 * v), 0, 1));
        animation.Add(0.33, 0.66, new Animation(v => dot.Opacity = 1 - (0.55 * v), 0, 1));

        var handle = $"nutrade.loading.{dot.Id}";
        if (delayMs == 0)
        {
            animation.Commit(this, handle, 16, CycleMs, repeat: () => _animating);
            return;
        }

        // Dispatcher rather than Task.Delay so the stagger cannot start after the control
        // has already been torn down.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(delayMs), () =>
        {
            if (_animating) animation.Commit(this, handle, 16, CycleMs, repeat: () => _animating);
        });
    }

    private void StopAnimation()
    {
        _animating = false;
        foreach (var dot in new View[] { Dot1, Dot2, Dot3 })
        {
            this.AbortAnimation($"nutrade.loading.{dot.Id}");
            dot.TranslationY = 0;
            dot.Opacity = 1;
        }
    }
}

namespace NUTrade1.ViewModels;

/// <summary>Ticks once a second on the UI dispatcher so a countdown label can move.</summary>
sealed class SecondClock : IDisposable
{
    private readonly IDispatcherTimer? _timer;

    public SecondClock(Action tick)
    {
        _timer = Application.Current?.Dispatcher.CreateTimer();
        if (_timer is null) return;

        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => tick();
        _timer.Start();
        tick();
    }

    public void Dispose() => _timer?.Stop();
}

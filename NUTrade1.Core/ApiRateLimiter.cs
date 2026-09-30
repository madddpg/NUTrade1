namespace NUTrade1.Core;

/// <summary>
/// Spaces out calls to Firebase so a burst — a feed refresh on top of a chat poll, a
/// payment check, a bid — cannot empty the project's quota or stall the student with
/// a raw "too many requests".
///
/// The steady polls (a listing every few seconds, chat a little faster) fit inside
/// <see cref="MaxCalls"/> per <see cref="Window"/>. Past that, the next call waits
/// until a slot frees, and <see cref="Throttled"/> fires so the UI can say that
/// paying, bidding, posting, or refreshing may take longer.
/// </summary>
public sealed class ApiRateLimiter
{
    public const int MaxCalls = 15;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    public const string ThrottleWarning =
        "NUTrade is spacing out requests so the connection stays reliable. Paying, bidding, posting, or refreshing can take longer until this clears.";

    private readonly Queue<long> _calls = new();
    private readonly object _gate = new();
    private readonly TimeSpan _warningCooldown;
    private long _lastWarningTicks;

    public ApiRateLimiter(TimeSpan? warningCooldown = null)
    {
        _warningCooldown = warningCooldown ?? TimeSpan.FromSeconds(20);
    }

    /// <summary>Raised, at most once per cooldown, when a call has to wait.</summary>
    public event Action<string>? Throttled;

    public async Task AcquireAsync(CancellationToken ct = default)
    {
        while (true)
        {
            TimeSpan wait;
            lock (_gate)
            {
                wait = TryTake(DateTimeOffset.UtcNow);
            }

            if (wait <= TimeSpan.Zero) return;

            ReportWait(wait);
            await Task.Delay(wait, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Records <paramref name="now"/> and returns <see cref="TimeSpan.Zero"/> when a slot
    /// is free. Otherwise returns how long until the oldest call leaves the window,
    /// without recording this one — the caller waits, then tries again.
    /// </summary>
    public TimeSpan TryTake(DateTimeOffset now)
    {
        var cutoff = (now - Window).UtcTicks;
        while (_calls.Count > 0 && _calls.Peek() <= cutoff) _calls.Dequeue();

        if (_calls.Count < MaxCalls)
        {
            _calls.Enqueue(now.UtcTicks);
            return TimeSpan.Zero;
        }

        var oldest = new DateTimeOffset(_calls.Peek(), TimeSpan.Zero);
        var wait = oldest + Window - now;
        return wait <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : wait;
    }

    /// <summary>Fires <see cref="Throttled"/> when a caller was asked to wait. Debounced.</summary>
    public void ReportWait(TimeSpan wait)
    {
        if (wait > TimeSpan.Zero) Warn();
    }

    private void Warn()
    {
        var now = DateTimeOffset.UtcNow.UtcTicks;
        bool raise;
        lock (_gate)
        {
            raise = now - _lastWarningTicks >= _warningCooldown.Ticks;
            if (raise) _lastWarningTicks = now;
        }

        if (raise) Throttled?.Invoke(ThrottleWarning);
    }
}

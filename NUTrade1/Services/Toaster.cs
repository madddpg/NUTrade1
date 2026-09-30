namespace NUTrade1.Services;

public enum ToastKind
{
    Success,
    Error,
    Info,
}

/// <summary>One transient message. <see cref="Duration"/> follows the usual mobile rule:
/// something that went wrong stays long enough to read twice.</summary>
public sealed record Toast(ToastKind Kind, string Message)
{
    public TimeSpan Duration => Kind switch
    {
        ToastKind.Error => TimeSpan.FromSeconds(5),
        // The request limiter's notice is an Info toast, and it has to stay up long
        // enough to read that paying or bidding may slow down.
        ToastKind.Info => TimeSpan.FromSeconds(6),
        _ => TimeSpan.FromSeconds(3),
    };
}

/// <summary>
/// App-wide queue behind the toast that slides in at the top of the screen. Every
/// success, failure and error message in the app goes through here — the ViewModels
/// still set <c>ErrorMessage</c> / <c>InfoMessage</c> as they always did, and
/// <see cref="NUTrade1.ViewModels.BaseViewModel"/> forwards them.
///
/// Distinct from <see cref="NotificationCenter"/>, which owns the persistent card with an
/// action button — "you won an auction" waits for the student, "check your connection"
/// does not.
/// </summary>
public sealed class Toaster
{
    /// <summary>Beyond this a burst is dropped rather than trapping the student behind a
    /// queue of toasts they have to wait out.</summary>
    private const int MaxQueued = 2;

    public static Toaster Current { get; } = new();

    private readonly Queue<Toast> _queue = new();
    private CancellationTokenSource? _countdown;

    /// <summary>What should be on screen now, or null.</summary>
    public Toast? Active { get; private set; }

    /// <summary>Raised on the UI thread whenever <see cref="Active"/> changes.</summary>
    public event EventHandler? Changed;

    public static void Success(string? message) => Current.Show(ToastKind.Success, message);
    public static void Error(string? message) => Current.Show(ToastKind.Error, message);
    public static void Info(string? message) => Current.Show(ToastKind.Info, message);

    /// <summary>
    /// Queues a message. Null and blank are ignored, which is what makes the
    /// <c>ErrorMessage = null</c> that every command runs before its work a no-op rather
    /// than an empty toast.
    /// </summary>
    public void Show(ToastKind kind, string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var toast = new Toast(kind, message.Trim());

        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Tapping a failing button twice should re-arm the same toast, not line up a
            // second identical one behind it.
            if (Active is { } active && active.Kind == toast.Kind && active.Message == toast.Message)
            {
                StartCountdown(active);
                return;
            }

            if (Active is null)
            {
                Active = toast;
                StartCountdown(toast);
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (_queue.Count < MaxQueued) _queue.Enqueue(toast);
        });
    }

    /// <summary>Closes <paramref name="toast"/> if it is still showing, and brings on the next.</summary>
    public void Dismiss(Toast toast) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (!ReferenceEquals(Active, toast)) return;

        _countdown?.Cancel();
        _countdown = null;

        Active = _queue.Count > 0 ? _queue.Dequeue() : null;
        if (Active is { } next) StartCountdown(next);
        Changed?.Invoke(this, EventArgs.Empty);
    });

    /// <summary>Drops everything — used on sign-out so one student never sees another's messages.</summary>
    public void Clear() => MainThread.BeginInvokeOnMainThread(() =>
    {
        _countdown?.Cancel();
        _countdown = null;
        _queue.Clear();
        Active = null;
        Changed?.Invoke(this, EventArgs.Empty);
    });

    /// <summary>
    /// Auto-dismiss lives here rather than in the host control so a toast expires on
    /// schedule even if the student navigates to a page mid-countdown.
    /// </summary>
    private void StartCountdown(Toast toast)
    {
        _countdown?.Cancel();
        var cts = new CancellationTokenSource();
        _countdown = cts;

        _ = Task.Delay(toast.Duration, cts.Token).ContinueWith(
            task =>
            {
                if (!task.IsCanceled) Dismiss(toast);
            },
            TaskScheduler.Default);
    }
}

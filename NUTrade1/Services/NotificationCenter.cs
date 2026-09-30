namespace NUTrade1.Services;

/// <summary>One in-app notification, drawn with the card from the design (image 7).</summary>
public sealed record AppNotification(
    string Title,
    string Body,
    string? ActionText = null,
    Func<Task>? Action = null);

/// <summary>
/// App-wide queue behind the notification card. Anything can <see cref="Show"/> a
/// notification; every page hosts a <c>NotificationHost</c>, and whichever page is on
/// screen draws the current one until it is dismissed. Several arriving together are
/// shown one after another rather than stacked.
/// </summary>
public sealed class NotificationCenter
{
    public static NotificationCenter Current { get; } = new();

    private readonly Queue<AppNotification> _queue = new();

    /// <summary>What should be on screen now, or null.</summary>
    public AppNotification? Active { get; private set; }

    /// <summary>Raised on the UI thread whenever <see cref="Active"/> changes.</summary>
    public event EventHandler? Changed;

    public void Show(AppNotification notification) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (Active is null) Active = notification;
        else _queue.Enqueue(notification);
        Changed?.Invoke(this, EventArgs.Empty);
    });

    /// <summary>Closes <paramref name="notification"/> (if it is still the active one) and brings on the next.</summary>
    public void Dismiss(AppNotification notification) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (!ReferenceEquals(Active, notification)) return;
        Active = _queue.Count > 0 ? _queue.Dequeue() : null;
        Changed?.Invoke(this, EventArgs.Empty);
    });

    /// <summary>Drops everything — used on sign-out so one student never sees another's news.</summary>
    public void Clear() => MainThread.BeginInvokeOnMainThread(() =>
    {
        _queue.Clear();
        Active = null;
        Changed?.Invoke(this, EventArgs.Empty);
    });
}

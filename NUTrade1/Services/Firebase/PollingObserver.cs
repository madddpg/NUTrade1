namespace NUTrade1.Services.Firebase;

/// <summary>
/// Stands in for a Firestore snapshot listener over the REST transport, which has no
/// streaming equivalent: re-reads on an interval and raises the callback on the UI
/// thread whenever the result changed.
///
/// It fires once immediately so a page paints without waiting out the first interval,
/// suppresses repeats by comparing a caller-supplied signature, and swallows transient
/// read failures — a dropped poll should not tear down the page, the next one recovers.
/// Disposing stops the loop; every caller does that in <c>OnDisappearing</c>.
/// </summary>
internal sealed class PollingObserver<T> : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    public PollingObserver(
        Func<CancellationToken, Task<T>> read,
        Func<T, string> signature,
        Action<T> onChanged,
        TimeSpan interval)
    {
        _ = RunAsync(read, signature, onChanged, interval, _cts.Token);
    }

    private static async Task RunAsync(
        Func<CancellationToken, Task<T>> read,
        Func<T, string> signature,
        Action<T> onChanged,
        TimeSpan interval,
        CancellationToken ct)
    {
        string? previous = null;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var value = await read(ct);
                var current = signature(value);

                if (current != previous)
                {
                    previous = current;
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (!ct.IsCancellationRequested) onChanged(value);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Transient — try again on the next tick.
            }

            try
            {
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}

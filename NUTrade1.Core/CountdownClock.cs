namespace NUTrade1.Core;

/// <summary>
/// One on-screen shape for every deadline: <c>MM:SS</c> under an hour, <c>HH:MM:SS</c>
/// at an hour or more, and <c>00:00</c> once the time has passed. Pages tick this once
/// a second from the server timestamp. A wall-clock time is not this format.
/// </summary>
public static class CountdownClock
{
    public static string Format(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "00:00";

        if (remaining.TotalHours >= 1)
        {
            var hours = (int)remaining.TotalHours;
            return $"{hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
        }

        return $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
    }

    public static string FormatUntil(DateTimeOffset? endsAt, DateTimeOffset now)
    {
        if (endsAt is not { } ends) return string.Empty;
        return Format(ends - now);
    }
}

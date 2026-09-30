using NUTrade1.Core;
using Xunit;

namespace NUTrade1.Tests;

public class ApiRateLimiterTests
{
    [Fact]
    public void Calls_inside_the_window_go_straight_through()
    {
        var limiter = new ApiRateLimiter();
        var now = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < ApiRateLimiter.MaxCalls; i++)
            Assert.Equal(TimeSpan.Zero, limiter.TryTake(now));
    }

    [Fact]
    public void The_next_call_waits_until_a_slot_frees()
    {
        var limiter = new ApiRateLimiter();
        var now = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < ApiRateLimiter.MaxCalls; i++)
            limiter.TryTake(now);

        Assert.Equal(ApiRateLimiter.Window, limiter.TryTake(now));
    }

    [Fact]
    public void A_call_after_the_window_is_free_again()
    {
        var limiter = new ApiRateLimiter();
        var now = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < ApiRateLimiter.MaxCalls; i++)
            limiter.TryTake(now);

        var later = now + ApiRateLimiter.Window + TimeSpan.FromMilliseconds(1);
        Assert.Equal(TimeSpan.Zero, limiter.TryTake(later));
    }

    [Fact]
    public void A_wait_raises_the_warning_once_per_cooldown()
    {
        var limiter = new ApiRateLimiter(TimeSpan.FromMinutes(1));
        var warnings = new List<string>();
        limiter.Throttled += warnings.Add;

        limiter.ReportWait(TimeSpan.FromSeconds(2));
        limiter.ReportWait(TimeSpan.FromSeconds(2));
        limiter.ReportWait(TimeSpan.Zero);

        Assert.Single(warnings);
        Assert.Contains("can take longer", warnings[0]);
    }
}

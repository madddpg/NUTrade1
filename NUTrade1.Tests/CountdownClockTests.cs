using NUTrade1.Core;
using Xunit;

namespace NUTrade1.Tests;

public class CountdownClockTests
{
    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(-3, "00:00")]
    [InlineData(45, "00:45")]
    [InlineData(90, "01:30")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "01:00:00")]
    [InlineData(24 * 3600, "24:00:00")]
    public void A_deadline_reads_as_a_clock(int seconds, string expected)
    {
        Assert.Equal(expected, CountdownClock.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void A_missing_deadline_is_blank()
    {
        Assert.Equal(string.Empty, CountdownClock.FormatUntil(null, DateTimeOffset.UnixEpoch));
    }
}

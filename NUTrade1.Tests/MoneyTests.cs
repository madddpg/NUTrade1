using NUTrade1.Core;
using Xunit;

namespace NUTrade1.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData(1000, "₱10.00")]
    [InlineData(2000, "₱20.00")]
    [InlineData(50, "₱0.50")]
    [InlineData(0, "₱0.00")]
    public void ToDisplay_formats_centavos_as_pesos(long centavos, string expected)
    {
        Assert.Equal(expected, Money.ToDisplay(centavos));
    }

    [Theory]
    [InlineData(10.00, 1000)]
    [InlineData(20.00, 2000)]
    [InlineData(0.01, 1)]
    public void FromPesos_round_trips_through_centavos(decimal pesos, long expectedCentavos)
    {
        Assert.Equal(expectedCentavos, Money.FromPesos(pesos));
    }
}

using System.Globalization;

namespace NUTrade1.Core;

/// <summary>
/// Helpers for money handled as whole centavos. NUTrade never stores fractional pesos
/// as floating point; a value of <c>1000</c> means ₱10.00.
/// </summary>
public static class Money
{
    public const long CentavosPerPeso = 100;

    public static long FromPesos(decimal pesos) => (long)Math.Round(pesos * CentavosPerPeso, MidpointRounding.AwayFromZero);

    public static decimal ToPesos(long centavos) => centavos / (decimal)CentavosPerPeso;

    /// <summary>Formats centavos as e.g. "₱10.00".</summary>
    public static string ToDisplay(long centavos) =>
        "₱" + ToPesos(centavos).ToString("0.00", CultureInfo.InvariantCulture);
}

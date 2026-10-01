namespace NUTrade1.Core;

/// <summary>
/// How a student's bid credit pays the next commitment deposit.
/// The server applies the same split. Credit never leaves the app as cash.
/// </summary>
public static class BidCredit
{
    /// <summary>The bond for a bid: <paramref name="percent"/> of <paramref name="bidCentavos"/>.</summary>
    public static long DepositFor(long bidCentavos, int percent)
    {
        if (bidCentavos <= 0 || percent <= 0) return 0;
        return bidCentavos * percent / 100;
    }

    /// <summary>
    /// <paramref name="Applied"/> is taken from the balance, up to the deposit.
    /// <paramref name="QrDue"/> is what still has to be scanned. Zero means the bid can go live with no QR.
    /// </summary>
    public static (long Applied, long QrDue) Split(long balanceCentavos, long depositCentavos)
    {
        if (depositCentavos <= 0) return (0, 0);
        var applied = balanceCentavos <= 0 ? 0 : Math.Min(balanceCentavos, depositCentavos);
        return (applied, depositCentavos - applied);
    }
}

namespace NUTrade1.Core;

/// <summary>
/// Maps to <c>counters/revenue</c>. Updated atomically by the PayMongo webhook with
/// <c>FieldValue.increment</c>; never written by the client.
/// </summary>
public sealed class RevenueCounter
{
    /// <summary>Lifetime listing-fee revenue, in centavos.</summary>
    public long TotalCentavos { get; set; }

    /// <summary>Per-package breakdown, keyed by <see cref="ListingPackage"/> name.</summary>
    public IDictionary<string, long> ByPackage { get; set; } = new Dictionary<string, long>();

    public DateTimeOffset UpdatedAt { get; set; }
}

namespace NUTrade1.Core;

/// <summary>
/// What a draft may store for each <see cref="ListingKind"/>. Mirrors the create rule in
/// firestore.rules: an auction needs a starting bid and an increment, a standard listing
/// needs a price and a zero increment, and a swap stores zeros for both.
/// </summary>
public static class ListingKindRules
{
    /// <summary>
    /// Turns the form's peso text into the centavo pair a draft is allowed to store.
    /// <paramref name="error"/> is the sentence to show when the text is not a valid price.
    /// </summary>
    public static (long StartingBidCentavos, long MinIncrementCentavos, string? Error) Normalize(
        ListingKind kind, string? priceText, string? incrementText)
    {
        if (kind == ListingKind.Swap)
            return (0, 0, null);

        if (!long.TryParse((priceText ?? string.Empty).Trim(), out var pesos) || pesos <= 0)
        {
            return (0, 0, kind == ListingKind.Standard
                ? "Enter a valid price."
                : "Enter a valid starting bid.");
        }

        if (kind == ListingKind.Standard)
            return (Money.FromPesos(pesos), 0, null);

        if (!long.TryParse((incrementText ?? string.Empty).Trim(), out var incrementPesos) || incrementPesos <= 0)
            return (0, 0, "Enter a valid bid increment.");

        return (Money.FromPesos(pesos), Money.FromPesos(incrementPesos), null);
    }
}

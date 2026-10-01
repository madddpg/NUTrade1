namespace NUTrade1.Core;

/// <summary>
/// Bidding, as it actually works: a bid is not placed until a commitment deposit behind it
/// has been paid.
///
/// <see cref="RequestBidAsync"/> validates the amount and hands back a QR code. Nothing
/// appears on the listing until <see cref="CheckDepositAsync"/> confirms the money landed
/// — and that polling is not belt-and-braces, it is the mechanism: PayMongo's webhook has
/// never arrived in this project, so the app asking is what places the bid.
///
/// The deposit is a bond against ghost bidding. Being outbid, losing, showing up, or the
/// seller cancelling returns it as bid credit. Winning and not showing up gives that
/// credit to the seller. Credit pays the next deposit and cannot be cashed out.
/// </summary>
public interface IDepositService
{
    /// <summary>
    /// Validates a bid and mints the deposit QR. Writes no bid — the student has to pay
    /// first. Fails when the amount is under the listing's current minimum.
    /// </summary>
    Task<OperationResult<BidDeposit>> RequestBidAsync(
        string listingId, long amountCentavos, CancellationToken ct = default);

    /// <summary>
    /// Asks the server whether the deposit has been paid, placing the bid if it has.
    /// The payment screen calls this on a timer while the QR is on screen.
    /// </summary>
    Task<OperationResult<BidDeposit>> CheckDepositAsync(string bidIntentId, CancellationToken ct = default);

    /// <summary>Every deposit the signed-in student has raised, newest first.</summary>
    Task<IReadOnlyList<BidDeposit>> GetMyDepositsAsync(int limit = 25, CancellationToken ct = default);
}

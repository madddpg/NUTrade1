namespace NUTrade1.Core;

/// <summary>
/// Reading bids and, for a listing owner, acting on them.
///
/// Placing one is not here — see <see cref="IDepositService"/>. A bid is written only when
/// its commitment deposit is confirmed paid, so there is no "place a bid" call to make.
/// <c>listings/{id}/bids</c> is server-write-only either way.
/// </summary>
public interface IBidService
{
    /// <summary>Bid history for one listing, newest first.</summary>
    Task<IReadOnlyList<Bid>> GetBidsForListingAsync(string listingId, CancellationToken ct = default);

    /// <summary>Bids the signed-in user has placed, across all listings.</summary>
    Task<IReadOnlyList<Bid>> GetMyBidsAsync(CancellationToken ct = default);

    /// <summary>Live bids on one of the signed-in user's listings, highest first.</summary>
    Task<IReadOnlyList<Bid>> GetIncomingBidsAsync(string listingId, CancellationToken ct = default);

    /// <summary>
    /// Approves a bid as the winner, ending the auction early. The Function opens the
    /// chat room and marks the listing matched.
    /// </summary>
    Task<OperationResult> ApproveBidAsync(string listingId, string bidId, CancellationToken ct = default);

    /// <summary>Rejects one bid; the auction continues with the next-highest promoted to the top.</summary>
    Task<OperationResult> DeclineBidAsync(string listingId, string bidId, CancellationToken ct = default);

    Task<OperationResult> WithdrawBidAsync(string listingId, string bidId, CancellationToken ct = default);
}

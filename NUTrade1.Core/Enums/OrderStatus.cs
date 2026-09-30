namespace NUTrade1.Core;

/// <summary>
/// Lifecycle of what a winning bidder owes the seller. Server-owned: every transition
/// is a Cloud Function, because the transitions <em>are</em> the sale.
/// </summary>
public enum OrderStatus
{
    /// <summary>Seller approved the bid; the buyer's payment window is running.</summary>
    AwaitingPayment = 0,

    /// <summary>
    /// Money confirmed by PayMongo's signed webhook. Never asserted by either student —
    /// that is the whole point of settling through the gateway rather than peer to peer.
    /// </summary>
    Paid,

    /// <summary>The window closed unpaid, the bid was struck out and the auction reopened.</summary>
    Released,

    Cancelled,
}

namespace NUTrade1.Core;

/// <summary>
/// The buyer-pays-the-seller half of a won auction, settled by PayMongo QR Ph.
///
/// There is no "I paid" button here on purpose: the order reaches
/// <see cref="OrderStatus.Paid"/> only when PayMongo's signed webhook says so, so the
/// app watches the order rather than asserting anything about it. Reads come from
/// Firestore; minting a code is a Cloud Function call.
/// </summary>
public interface IOrderService
{
    /// <summary>The order for one listing that the signed-in user is a party to, if any.</summary>
    Task<Order?> GetOrderForListingAsync(string listingId, CancellationToken ct = default);

    Task<Order?> GetOrderAsync(string orderId, CancellationToken ct = default);

    /// <summary>Orders the signed-in user owes or is owed, newest first.</summary>
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(CancellationToken ct = default);

    /// <summary>
    /// Mints a QR Ph code for the winning bidder to scan, or returns the current one
    /// while it is still valid. Safe to call repeatedly — the code lapses in about ten
    /// minutes while the order stands for a day.
    /// </summary>
    Task<OperationResult<Order>> CreateQrPaymentAsync(string orderId, CancellationToken ct = default);

    /// <summary>
    /// Watches one order so the payment screen flips to paid the moment the webhook
    /// lands, without the student having to do anything.
    /// </summary>
    IDisposable ObserveOrder(string orderId, Action<Order?> onChanged);
}

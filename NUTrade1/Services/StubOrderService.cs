using NUTrade1.Core;

namespace NUTrade1.Services;

/// <summary>
/// Offline stand-in for <see cref="IOrderService"/>. Starts empty: real orders are created
/// server-side when an auction is awarded, and settled by PayMongo's webhook — neither of
/// which can happen without a network.
/// </summary>
public sealed class StubOrderService : IOrderService
{
    private readonly Dictionary<string, Order> _orders = new();

    public Task<Order?> GetOrderAsync(string orderId, CancellationToken ct = default) =>
        Task.FromResult(_orders.TryGetValue(orderId, out var o) ? o : null);

    public Task<Order?> GetOrderForListingAsync(string listingId, CancellationToken ct = default) =>
        Task.FromResult(_orders.Values.FirstOrDefault(o => o.ListingId == listingId));

    public Task<IReadOnlyList<Order>> GetMyOrdersAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Order>>(_orders.Values.ToArray());

    public Task<OperationResult<Order>> CreateQrPaymentAsync(
        string orderId, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<Order>.Fail("Paying needs a network connection."));

    public IDisposable ObserveOrder(string orderId, Action<Order?> onChanged)
    {
        onChanged(_orders.TryGetValue(orderId, out var o) ? o : null);
        return new NoopDisposable();
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

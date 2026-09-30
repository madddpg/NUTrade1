using NUTrade1.Core;

namespace NUTrade1.Services;

/// <summary>
/// Offline stand-in for <see cref="IBidService"/>. Starts empty and reproduces just
/// enough of <c>placeBid</c>'s validation to exercise the bidding UI without a network.
/// </summary>
public sealed class StubBidService : IBidService
{
    private readonly StubListingService _listings;
    private readonly IAuthService _auth;
    private readonly List<Bid> _bids = new();

    public StubBidService(IListingService listings, IAuthService auth)
    {
        _listings = (StubListingService)listings;
        _auth = auth;
    }

    public Task<IReadOnlyList<Bid>> GetBidsForListingAsync(string listingId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Bid>>(
            _bids.Where(b => b.ListingId == listingId).OrderByDescending(b => b.CreatedAt).ToArray());

    public Task<IReadOnlyList<Bid>> GetMyBidsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Bid>>(
            _bids.Where(b => b.BidderUid == _auth.CurrentUid)
                .OrderByDescending(b => b.CreatedAt).ToArray());

    public Task<IReadOnlyList<Bid>> GetIncomingBidsAsync(string listingId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Bid>>(
            _bids.Where(b => b.ListingId == listingId && b.Status == BidStatus.Pending)
                .OrderByDescending(b => b.AmountCentavos).ToArray());

    public Task<OperationResult> ApproveBidAsync(string listingId, string bidId, CancellationToken ct = default) =>
        SetStatus(bidId, BidStatus.Approved);

    public Task<OperationResult> DeclineBidAsync(string listingId, string bidId, CancellationToken ct = default) =>
        SetStatus(bidId, BidStatus.Declined);

    public Task<OperationResult> WithdrawBidAsync(string listingId, string bidId, CancellationToken ct = default) =>
        SetStatus(bidId, BidStatus.Withdrawn);

    private Task<OperationResult> SetStatus(string bidId, BidStatus status)
    {
        if (_bids.FirstOrDefault(b => b.Id == bidId) is { } bid)
            bid.Status = status;
        return Task.FromResult(OperationResult.Ok());
    }
}

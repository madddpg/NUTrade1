namespace NUTrade1.Core;

/// <summary>Lifecycle of a single bid placed against an active auction listing.</summary>
public enum BidStatus
{
    /// <summary>Placed and currently the highest bid, awaiting the seller's approval to match.</summary>
    Pending = 0,

    /// <summary>Seller approved this bid as the winning one; a chat room is opened.</summary>
    Approved,

    Declined,

    /// <summary>A higher bid was placed after this one.</summary>
    Outbid,

    Withdrawn,
}

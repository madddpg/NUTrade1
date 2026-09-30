namespace NUTrade1.Core;

/// <summary>
/// Lifecycle of an auction listing. The happy path is
/// <c>Draft → PendingPayment → PendingApproval → Active → Matched → Completed</c>.
/// Only the Admin SDK (a Cloud Function) may move a listing to <see cref="Active"/>,
/// and only after an admin has approved it.
/// </summary>
public enum ListingStatus
{
    Draft = 0,
    PendingPayment,
    Active,
    Matched,
    Completed,
    Expired,
    Cancelled,

    /// <summary>Fee settled (or free) — waiting for an admin to approve it before it goes live.</summary>
    PendingApproval,

    /// <summary>An admin turned it down. Terminal; see <see cref="Listing.RejectionReason"/>.</summary>
    Rejected,
}

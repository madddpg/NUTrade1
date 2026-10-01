namespace NUTrade1.Core;

/// <summary>Where a payout is to be sent. The app stores only what the student typed.</summary>
public sealed record PayoutDestination(PayoutMethod Method, string AccountName, string AccountNumber);

/// <summary>
/// The student's internal balance and the record behind it.
///
/// The balance is bid credit. It changes only when a deposit is reserved, returned, or
/// awarded after a no-show, all server-side. Students cannot cash it out.
/// </summary>
public interface IWalletService
{
    /// <summary>The signed-in student's balance. A wallet that has never been credited reads zero.</summary>
    Task<Wallet> GetWalletAsync(CancellationToken ct = default);

    /// <summary>Newest first — the audit trail behind the balance.</summary>
    Task<IReadOnlyList<LedgerEntry>> GetLedgerAsync(int limit = 25, CancellationToken ct = default);

    /// <summary>The student's own payout requests, newest first.</summary>
    Task<IReadOnlyList<PayoutRequest>> GetMyPayoutsAsync(int limit = 10, CancellationToken ct = default);

    /// <summary>Cashes the whole balance out. Fails when it is under the minimum.</summary>
    Task<OperationResult> RequestPayoutAsync(PayoutDestination destination, CancellationToken ct = default);
}

/// <summary>Admin side of the payout queue. Every action is re-checked by its Cloud Function.</summary>
public interface IPayoutModerationService
{
    /// <summary>Everything still waiting on an admin, oldest first.</summary>
    Task<IReadOnlyList<PayoutRequest>> GetPendingPayoutsAsync(CancellationToken ct = default);

    /// <summary>Records that the money has been sent off-platform.</summary>
    Task<OperationResult> MarkPaidAsync(string payoutRequestId, CancellationToken ct = default);

    /// <summary>Refuses the request and returns the held balance to the student.</summary>
    Task<OperationResult> DeclineAsync(string payoutRequestId, string? reason, CancellationToken ct = default);
}

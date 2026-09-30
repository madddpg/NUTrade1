namespace NUTrade1.Core;

/// <summary>Where the emailed one-time code was sent, and how long the student has to use it.</summary>
public sealed record OtpChallenge(string SentTo, int ExpiresInSeconds, int ResendAfterSeconds);

/// <summary>
/// The registration gate. Students sign up with any email or a Google account and prove
/// they own the address with a six-digit code; passing it grants the <c>verified</c>
/// claim that Firestore rules and the Cloud Functions check before allowing a listing
/// or a bid. There is no Student ID step in this path.
/// </summary>
public interface IEmailVerificationService
{
    /// <summary>
    /// Mails a fresh code to the signed-in account's own address and reports it back
    /// masked, so the app can show where to look without echoing the full address.
    /// </summary>
    Task<OperationResult<OtpChallenge>> SendCodeAsync(CancellationToken ct = default);

    /// <summary>
    /// Submits a code. On success the account is verified and the session's ID token is
    /// refreshed, so <see cref="IAuthService.IsVerified"/> is true by the time this returns.
    /// </summary>
    Task<OperationResult> VerifyCodeAsync(string code, CancellationToken ct = default);
}

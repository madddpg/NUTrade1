namespace NUTrade1.Core;

/// <summary>What the last password-reset step submits.</summary>
public sealed record PasswordResetDetails(string Email, string ResetToken, string Password);

/// <summary>
/// Forgot password, the same shape as registration: email → six-digit code → new
/// password. Nothing about the account changes until the code is proven, and setting the
/// new password signs every other device out.
///
/// Firebase Auth's own reset email is not used — it would come from Google rather than
/// Brevo and would send the student out to a web page. See functions/src/passwordReset.ts.
/// </summary>
public interface IPasswordResetService
{
    /// <summary>
    /// Mails a code to an address that has an account, and reports it back masked.
    /// Succeeds either way: an address with no account gets the same answer and no
    /// email, so this cannot be used to find out who has an account.
    /// </summary>
    Task<OperationResult<OtpChallenge>> StartAsync(string email, CancellationToken ct = default);

    /// <summary>Checks the code; on success returns the one-time token <see cref="CompleteAsync"/> needs.</summary>
    Task<OperationResult<string>> VerifyCodeAsync(string email, string code, CancellationToken ct = default);

    /// <summary>Sets the new password. The student signs in with it afterwards.</summary>
    Task<OperationResult> CompleteAsync(PasswordResetDetails details, CancellationToken ct = default);
}

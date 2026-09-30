namespace NUTrade1.Core;

/// <summary>What the last registration step submits.</summary>
public sealed record SignupDetails(
    string Email,
    string SignupToken,
    string FirstName,
    string LastName,
    string Program,
    string Password);

/// <summary>
/// Registration, code first: email → six-digit code → first and last name and a
/// password → account. Nothing is created until the code is proven, and the account is
/// born verified, so the student goes straight from here to signing in.
/// </summary>
public interface IRegistrationService
{
    /// <summary>Mails a code to an address that has no account yet.</summary>
    Task<OperationResult<OtpChallenge>> StartAsync(string email, CancellationToken ct = default);

    /// <summary>Checks the code; on success returns the one-time token <see cref="CompleteAsync"/> needs.</summary>
    Task<OperationResult<string>> VerifyCodeAsync(string email, string code, CancellationToken ct = default);

    /// <summary>Creates the account and its profile. The student signs in afterwards.</summary>
    Task<OperationResult> CompleteAsync(SignupDetails details, CancellationToken ct = default);
}

using NUTrade1.Core;

namespace NUTrade1.Services;

/// <summary>
/// Offline stand-in for <see cref="IPasswordResetService"/>. No email is sent and any six
/// digits pass. Setting the new password is a no-op because <see cref="StubAuthService"/>
/// never checks one — the point is only that the three screens can be clicked through.
/// </summary>
public sealed class StubPasswordResetService : IPasswordResetService
{
    public Task<OperationResult<OtpChallenge>> StartAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(NUTradeConstants.IsValidEmail(email)
            ? OperationResult<OtpChallenge>.Ok(new OtpChallenge(email.Trim(), 600, 60))
            : OperationResult<OtpChallenge>.Fail("Enter a valid email address."));

    public Task<OperationResult<string>> VerifyCodeAsync(string email, string code, CancellationToken ct = default) =>
        Task.FromResult(code.Trim().Length == 6
            ? OperationResult<string>.Ok("offline-reset-token")
            : OperationResult<string>.Fail("Enter the 6-digit code from your email."));

    public Task<OperationResult> CompleteAsync(PasswordResetDetails details, CancellationToken ct = default)
    {
        StubAuthService.Register(details.Email);
        return Task.FromResult(OperationResult.Ok());
    }
}

using NUTrade1.Core;

namespace NUTrade1.Services;

/// <summary>
/// Offline stand-in for <see cref="IEmailVerificationService"/>. There is no mail
/// server behind it, so it accepts one fixed code — enough to walk the registration
/// flow end to end with no network.
/// </summary>
public sealed class StubEmailVerificationService : IEmailVerificationService
{
    /// <summary>The code the offline build always expects.</summary>
    public const string OfflineCode = "000000";

    private readonly IAuthService _auth;

    public StubEmailVerificationService(IAuthService auth) => _auth = auth;

    public Task<OperationResult<OtpChallenge>> SendCodeAsync(CancellationToken ct = default) =>
        Task.FromResult(OperationResult<OtpChallenge>.Ok(
            new OtpChallenge($"{_auth.CurrentEmail} (offline — the code is {OfflineCode})", 600, 0)));

    public Task<OperationResult> VerifyCodeAsync(string code, CancellationToken ct = default)
    {
        if (code.Trim() != OfflineCode)
            return Task.FromResult(OperationResult.Fail($"Offline builds only accept {OfflineCode}."));

        if (_auth is StubAuthService stub) stub.MarkVerified();
        return Task.FromResult(OperationResult.Ok());
    }
}

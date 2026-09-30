using NUTrade1.Core;

namespace NUTrade1.Services;

/// <summary>
/// Offline stand-in for <see cref="IRegistrationService"/>. No email is sent: any six
/// digits pass, and completing registers the address with <see cref="StubAuthService"/>
/// so signing in with it afterwards lands on the feed, as it does online.
/// </summary>
public sealed class StubRegistrationService : IRegistrationService
{
    private readonly IUserService _users;

    public StubRegistrationService(IUserService users) => _users = users;

    public Task<OperationResult<OtpChallenge>> StartAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(NUTradeConstants.IsValidEmail(email)
            ? OperationResult<OtpChallenge>.Ok(new OtpChallenge(email.Trim(), 600, 60))
            : OperationResult<OtpChallenge>.Fail("Enter a valid email address."));

    public Task<OperationResult<string>> VerifyCodeAsync(string email, string code, CancellationToken ct = default) =>
        Task.FromResult(code.Trim().Length == 6
            ? OperationResult<string>.Ok("offline-signup-token")
            : OperationResult<string>.Fail("Enter the 6-digit code from your email."));

    public Task<OperationResult> CompleteAsync(SignupDetails details, CancellationToken ct = default)
    {
        StubAuthService.Register(details.Email);
        (_users as StubUserService)?.Seed(new UserProfile
        {
            Uid = StubAuthService.OfflineUid(details.Email),
            Email = details.Email.Trim(),
            FirstName = details.FirstName.Trim(),
            LastName = details.LastName.Trim(),
            DisplayName = $"{details.FirstName.Trim()} {details.LastName.Trim()}",
            Program = details.Program,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return Task.FromResult(OperationResult.Ok());
    }
}

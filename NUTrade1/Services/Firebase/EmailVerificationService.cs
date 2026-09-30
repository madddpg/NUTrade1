using System.Text.Json;
using NUTrade1.Core;

namespace NUTrade1.Services.Firebase;

/// <summary>
/// <see cref="IEmailVerificationService"/> over the <c>sendEmailOtp</c> /
/// <c>verifyEmailOtp</c> callables.
///
/// Kept out of <see cref="FirebaseAuthService"/> on purpose: that class is the
/// <see cref="IFirebaseTokenProvider"/> every transport depends on, so having it depend
/// on <see cref="FunctionsClient"/> in turn would close a dependency cycle.
/// </summary>
public sealed class EmailVerificationService : IEmailVerificationService
{
    private readonly FunctionsClient _functions;
    private readonly IAuthService _auth;

    public EmailVerificationService(FunctionsClient functions, IAuthService auth)
    {
        _functions = functions;
        _auth = auth;
    }

    public async Task<OperationResult<OtpChallenge>> SendCodeAsync(CancellationToken ct = default)
    {
        var result = await _functions.CallAsync("sendEmailOtp", new { }, ct);
        if (!result.Succeeded) return OperationResult<OtpChallenge>.Fail(result.Error!);

        var payload = result.Value;
        var sentTo = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("sentTo", out var to)
            ? to.GetString() ?? "your email"
            : "your email";

        return OperationResult<OtpChallenge>.Ok(new OtpChallenge(
            sentTo,
            ReadInt(payload, "expiresInSeconds", 600),
            ReadInt(payload, "resendAfterSeconds", 60)));
    }

    public async Task<OperationResult> VerifyCodeAsync(string code, CancellationToken ct = default)
    {
        var result = await _functions.CallAsync("verifyEmailOtp", new { code = code.Trim() }, ct);
        if (!result.Succeeded) return OperationResult.Fail(result.Error!);

        // The claim was granted server-side a moment ago, but this session is still
        // holding the ID token it was issued at sign-in. Without this the app would keep
        // believing the account is unverified until the token happened to expire.
        await _auth.RefreshClaimsAsync(ct, force: true);

        return OperationResult.Ok();
    }

    private static int ReadInt(JsonElement payload, string name, int fallback) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(name, out var value)
        && value.TryGetInt32(out var parsed)
            ? parsed
            : fallback;
}

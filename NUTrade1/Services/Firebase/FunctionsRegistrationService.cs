using System.Text.Json;
using NUTrade1.Core;

namespace NUTrade1.Services.Firebase;

/// <summary>
/// <see cref="IRegistrationService"/> over the <c>startSignup</c> / <c>verifySignupCode</c> /
/// <c>completeSignup</c> callables. All three are called signed out — there is no
/// account to sign in with until the last one succeeds.
/// </summary>
public sealed class FunctionsRegistrationService : IRegistrationService
{
    private readonly FunctionsClient _functions;

    public FunctionsRegistrationService(FunctionsClient functions) => _functions = functions;

    public async Task<OperationResult<OtpChallenge>> StartAsync(string email, CancellationToken ct = default)
    {
        var result = await _functions.CallAsync("startSignup", new { email = email.Trim() }, ct, signedOut: true);
        if (!result.Succeeded) return OperationResult<OtpChallenge>.Fail(result.Error!);

        var payload = result.Value;
        return OperationResult<OtpChallenge>.Ok(new OtpChallenge(
            ReadString(payload, "sentTo") ?? email.Trim(),
            ReadInt(payload, "expiresInSeconds", 600),
            ReadInt(payload, "resendAfterSeconds", 60)));
    }

    public async Task<OperationResult<string>> VerifyCodeAsync(string email, string code, CancellationToken ct = default)
    {
        var result = await _functions.CallAsync(
            "verifySignupCode", new { email = email.Trim(), code = code.Trim() }, ct, signedOut: true);
        if (!result.Succeeded) return OperationResult<string>.Fail(result.Error!);

        return ReadString(result.Value, "signupToken") is { Length: > 0 } token
            ? OperationResult<string>.Ok(token)
            : OperationResult<string>.Fail("NUTrade sent back something unexpected. Try again.");
    }

    public async Task<OperationResult> CompleteAsync(SignupDetails details, CancellationToken ct = default)
    {
        var result = await _functions.CallAsync("completeSignup", new
        {
            email = details.Email.Trim(),
            signupToken = details.SignupToken,
            firstName = details.FirstName.Trim(),
            lastName = details.LastName.Trim(),
            program = details.Program,
            password = details.Password,
        }, ct, signedOut: true);

        return result.Succeeded ? OperationResult.Ok() : OperationResult.Fail(result.Error!);
    }

    private static string? ReadString(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt(JsonElement payload, string name, int fallback) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(name, out var value)
        && value.TryGetInt32(out var parsed)
            ? parsed
            : fallback;
}

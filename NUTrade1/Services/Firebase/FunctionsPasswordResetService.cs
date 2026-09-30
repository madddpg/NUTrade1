using System.Text.Json;
using NUTrade1.Core;

namespace NUTrade1.Services.Firebase;

/// <summary>
/// <see cref="IPasswordResetService"/> over the <c>startPasswordReset</c> /
/// <c>verifyPasswordResetCode</c> / <c>completePasswordReset</c> callables. All three are
/// called signed out — a student who could sign in would not be here.
/// </summary>
public sealed class FunctionsPasswordResetService : IPasswordResetService
{
    private readonly FunctionsClient _functions;

    public FunctionsPasswordResetService(FunctionsClient functions) => _functions = functions;

    public async Task<OperationResult<OtpChallenge>> StartAsync(string email, CancellationToken ct = default)
    {
        var result = await _functions.CallAsync(
            "startPasswordReset", new { email = email.Trim() }, ct, signedOut: true);
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
            "verifyPasswordResetCode", new { email = email.Trim(), code = code.Trim() }, ct, signedOut: true);
        if (!result.Succeeded) return OperationResult<string>.Fail(result.Error!);

        return ReadString(result.Value, "resetToken") is { Length: > 0 } token
            ? OperationResult<string>.Ok(token)
            : OperationResult<string>.Fail("NUTrade sent back something unexpected. Try again.");
    }

    public async Task<OperationResult> CompleteAsync(PasswordResetDetails details, CancellationToken ct = default)
    {
        var result = await _functions.CallAsync("completePasswordReset", new
        {
            email = details.Email.Trim(),
            resetToken = details.ResetToken,
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

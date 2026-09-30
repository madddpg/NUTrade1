using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NUTrade1.Core;

namespace NUTrade1.Services.Firebase;

/// <summary>
/// Invokes Firebase callable Cloud Functions over plain HTTPS.
///
/// The callable protocol is simple enough to speak directly: POST
/// <c>{"data": {...}}</c> with the user's ID token as a bearer, and read back either
/// <c>{"result": {...}}</c> or <c>{"error": {"message": "...", "status": "..."}}</c>.
/// Doing it here rather than through a native SDK is what lets the Windows head
/// exercise the same payment, bidding and matching paths as the mobile heads.
/// </summary>
public sealed class FunctionsClient
{
    private readonly HttpClient _http;
    private readonly IFirebaseTokenProvider _tokens;
    private readonly ApiRateLimiter _limiter;

    public FunctionsClient(HttpClient http, IFirebaseTokenProvider tokens, ApiRateLimiter limiter)
    {
        _http = http;
        _tokens = tokens;
        _limiter = limiter;
    }

    /// <summary>
    /// Calls <paramref name="name"/> and returns its <c>result</c> payload. Failures come
    /// back as a failed <see cref="OperationResult{T}"/> carrying the Function's own
    /// message — those are written to be shown to the student (for example
    /// "Your bid must be at least ₱480.00").
    /// </summary>
    /// <param name="signedOut">
    /// True for the few Functions a signed-out student may call (registration). The call
    /// goes out with no ID token rather than failing with "Sign in to continue."
    /// </param>
    public async Task<OperationResult<JsonElement>> CallAsync(
        string name, object data, CancellationToken ct = default, bool signedOut = false)
    {
        string? token = null;
        if (!signedOut)
        {
            token = await _tokens.GetIdTokenAsync(ct);
            if (token is null) return OperationResult<JsonElement>.Fail("Sign in to continue.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, FirebaseSettings.FunctionUrl(name))
        {
            Content = JsonContent.Create(new Dictionary<string, object?> { ["data"] = data }),
        };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            await _limiter.AcquireAsync(ct);
            using var response = await _http.SendAsync(request, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
                return OperationResult<JsonElement>.Fail(message ?? "That didn't work. Try again.");
            }

            if (!response.IsSuccessStatusCode)
                return OperationResult<JsonElement>.Fail("That didn't work. Try again.");

            return OperationResult<JsonElement>.Ok(
                root.TryGetProperty("result", out var result) ? result.Clone() : default);
        }
        catch (HttpRequestException)
        {
            return OperationResult<JsonElement>.Fail("Can't reach NUTrade right now. Check your connection.");
        }
        catch (JsonException)
        {
            return OperationResult<JsonElement>.Fail("NUTrade sent back something unexpected. Try again.");
        }
    }
}

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using NUTrade1.Core;

namespace NUTrade1.Services.Firebase;

/// <summary>
/// <see cref="IAuthService"/> over the Firebase Auth REST API (Identity Toolkit).
///
/// Deliberately transport-level rather than a native SDK binding: the same code
/// then runs on every head this app targets, Windows included, which keeps the
/// desktop debug loop usable. The refresh token is the only thing persisted, in
/// <see cref="SecureStorage"/>; ID tokens are short-lived and stay in memory.
/// </summary>
public sealed class FirebaseAuthService : IAuthService, IFirebaseTokenProvider
{
    private const string RefreshTokenKey = "nutrade.refresh_token";

    /// <summary>Refresh this far ahead of expiry so an in-flight request can't age out mid-call.</summary>
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How stale the claims may be before <see cref="RefreshClaimsAsync"/> goes to the
    /// network. Profile calls it on every appearance, and an unconditional refresh there
    /// meant a securetoken round trip — plus the AuthStateChanged it raises, which makes
    /// the Shell re-evaluate its gate — every single time the tab was opened. A minute is
    /// well inside how fast an admin revocation needs to land.
    /// </summary>
    private static readonly TimeSpan ClaimsFreshFor = TimeSpan.FromMinutes(1);

    private readonly HttpClient _http;
    private readonly IGoogleAuthBroker _google;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private string? _idToken;
    private string? _refreshToken;
    private DateTimeOffset _expiresAt;
    private DateTimeOffset _claimsReadAt;
    private Dictionary<string, JsonElement> _claims = new();

    public FirebaseAuthService(HttpClient http, IGoogleAuthBroker google)
    {
        _http = http;
        _google = google;
    }

    public string? CurrentUid { get; private set; }
    public string? CurrentEmail { get; private set; }
    public bool IsSignedIn => CurrentUid is not null;

    public bool IsGoogleSignInAvailable => _google.IsConfigured;

    public bool IsVerified =>
        _claims.TryGetValue("verified", out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>True when the signed-in account carries the admin role claim.</summary>
    public bool IsAdmin =>
        _claims.TryGetValue("role", out var r) && r.ValueKind == JsonValueKind.String && r.GetString() == "admin";

    public event EventHandler? AuthStateChanged;

    public async Task RestoreSessionAsync(CancellationToken ct = default)
    {
        if (IsSignedIn) return;

        var stored = await ReadStoredRefreshTokenAsync();
        if (string.IsNullOrWhiteSpace(stored)) return;

        _refreshToken = stored;
        try
        {
            await RefreshAsync(ct);
            AuthStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // A revoked or expired refresh token just means "signed out" — clear it
            // and let the Shell gate route to Login.
            await ClearSessionAsync();
        }
    }

    public Task<OperationResult> SignInWithEmailAsync(string email, string password, CancellationToken ct = default) =>
        AuthenticateAsync("signInWithPassword", email, password, ct);

    public async Task<OperationResult> SignUpWithEmailAsync(string email, string password, CancellationToken ct = default)
    {
        if (!NUTradeConstants.IsValidEmail(email))
            return OperationResult.Fail("Enter a valid email address.");

        return await AuthenticateAsync("signUp", email, password, ct);
    }

    public async Task<OperationResult> SignInWithGoogleAsync(CancellationToken ct = default)
    {
        var token = await _google.GetIdTokenAsync(ct);
        if (!token.Succeeded) return OperationResult.Fail(token.Error!);

        // Hand Google's assertion to Firebase, which mints its own session for it and
        // links or creates the NUTrade account behind the same email.
        var url = $"{FirebaseSettings.IdentityToolkit}:signInWithIdp?key={FirebaseSettings.ApiKey}";
        var payload = new
        {
            postBody = $"id_token={token.Value}&providerId=google.com",
            requestUri = "http://localhost",
            returnSecureToken = true,
        };

        try
        {
            using var response = await _http.PostAsJsonAsync(url, payload, ct);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

            if (!response.IsSuccessStatusCode) return OperationResult.Fail(DescribeError(doc.RootElement));

            var root = doc.RootElement;
            ApplyTokens(
                root.GetProperty("idToken").GetString()!,
                root.GetProperty("refreshToken").GetString()!,
                int.Parse(root.GetProperty("expiresIn").GetString()!));

            await SaveRefreshTokenAsync(_refreshToken!);
            AuthStateChanged?.Invoke(this, EventArgs.Empty);
            return OperationResult.Ok();
        }
        catch (HttpRequestException)
        {
            return OperationResult.Fail("Can't reach NUTrade right now. Check your connection.");
        }
    }

    /// <summary>
    /// Re-reads the claims, skipping the network when they were read moments ago. Pass
    /// <paramref name="force"/> when the caller has just changed them server-side and
    /// needs the new value now — verifying an email, for instance.
    /// </summary>
    public async Task RefreshClaimsAsync(CancellationToken ct = default, bool force = false)
    {
        if (_refreshToken is null) return;
        if (!force && DateTimeOffset.UtcNow - _claimsReadAt < ClaimsFreshFor) return;

        await RefreshAsync(ct);
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        await ClearSessionAsync();
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<string?> GetIdTokenAsync(CancellationToken ct = default)
    {
        if (_refreshToken is null) return null;
        if (_idToken is not null && DateTimeOffset.UtcNow < _expiresAt - RefreshSkew) return _idToken;

        await _refreshLock.WaitAsync(ct);
        try
        {
            // Another caller may have refreshed while we waited.
            if (_idToken is not null && DateTimeOffset.UtcNow < _expiresAt - RefreshSkew) return _idToken;
            await RefreshAsync(ct);
            return _idToken;
        }
        catch
        {
            return null;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<OperationResult> AuthenticateAsync(
        string endpoint, string email, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return OperationResult.Fail("Enter your email and password.");

        var url = $"{FirebaseSettings.IdentityToolkit}:{endpoint}?key={FirebaseSettings.ApiKey}";
        var payload = new { email = email.Trim(), password, returnSecureToken = true };

        try
        {
            using var response = await _http.PostAsJsonAsync(url, payload, ct);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

            if (!response.IsSuccessStatusCode)
                return OperationResult.Fail(DescribeError(doc.RootElement));

            var root = doc.RootElement;
            ApplyTokens(
                root.GetProperty("idToken").GetString()!,
                root.GetProperty("refreshToken").GetString()!,
                int.Parse(root.GetProperty("expiresIn").GetString()!));

            await SaveRefreshTokenAsync(_refreshToken!);
            AuthStateChanged?.Invoke(this, EventArgs.Empty);
            return OperationResult.Ok();
        }
        catch (HttpRequestException)
        {
            return OperationResult.Fail("Can't reach NUTrade right now. Check your connection.");
        }
    }

    /// <summary>Exchanges the refresh token for a fresh ID token. Throws if the token is dead.</summary>
    private async Task RefreshAsync(CancellationToken ct)
    {
        var url = $"{FirebaseSettings.SecureTokenEndpoint}?key={FirebaseSettings.ApiKey}";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = _refreshToken!,
        });

        using var response = await _http.PostAsync(url, content, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        ApplyTokens(
            root.GetProperty("id_token").GetString()!,
            root.GetProperty("refresh_token").GetString()!,
            int.Parse(root.GetProperty("expires_in").GetString()!));

        await SaveRefreshTokenAsync(_refreshToken!);
    }

    private void ApplyTokens(string idToken, string refreshToken, int expiresInSeconds)
    {
        _idToken = idToken;
        _refreshToken = refreshToken;
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);
        _claims = ReadClaims(idToken);
        _claimsReadAt = DateTimeOffset.UtcNow;

        CurrentUid = _claims.TryGetValue("user_id", out var uid) ? uid.GetString()
            : _claims.TryGetValue("sub", out var sub) ? sub.GetString()
            : null;
        CurrentEmail = _claims.TryGetValue("email", out var email) ? email.GetString() : null;
    }

    private async Task ClearSessionAsync()
    {
        _idToken = null;
        _refreshToken = null;
        _expiresAt = default;
        _claims = new Dictionary<string, JsonElement>();
        _claimsReadAt = default;
        CurrentUid = null;
        CurrentEmail = null;
        await SaveRefreshTokenAsync(null);
    }

    /// <summary>
    /// Reads the ID token's payload segment. The token was minted by Google and is
    /// re-verified server-side on every call, so the client decodes it purely to know
    /// which UI to show — it is never the basis of an authorisation decision here.
    /// </summary>
    private static Dictionary<string, JsonElement> ReadClaims(string idToken)
    {
        var parts = idToken.Split('.');
        if (parts.Length < 2) return new Dictionary<string, JsonElement>();

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                   ?? new Dictionary<string, JsonElement>();
        }
        catch
        {
            return new Dictionary<string, JsonElement>();
        }
    }

    /// <summary>Turns Identity Toolkit's SCREAMING_SNAKE codes into something a student can act on.</summary>
    private static string DescribeError(JsonElement root)
    {
        var code = root.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message)
            ? message.GetString() ?? string.Empty
            : string.Empty;

        // WEAK_PASSWORD arrives as "WEAK_PASSWORD : Password should be at least 6 characters".
        var key = code.Split(':')[0].Trim();

        return key switch
        {
            "EMAIL_EXISTS" => "That email already has an account — sign in instead.",
            "EMAIL_NOT_FOUND" or "INVALID_PASSWORD" or "INVALID_LOGIN_CREDENTIALS" =>
                "Email or password is incorrect.",
            "INVALID_EMAIL" => "That doesn't look like a valid email address.",
            "WEAK_PASSWORD" => "Password must be at least 6 characters.",
            "USER_DISABLED" => "This account has been disabled. Contact the NUTrade admins.",
            "TOO_MANY_ATTEMPTS_TRY_LATER" => "Too many attempts. Try again in a few minutes.",
            "OPERATION_NOT_ALLOWED" or "CONFIGURATION_NOT_FOUND" =>
                "Email sign-in isn't enabled on this Firebase project yet.",
            _ => string.IsNullOrEmpty(key) ? "Sign-in failed. Try again." : $"Sign-in failed ({key}).",
        };
    }

    /// <summary>
    /// SecureStorage is unavailable on some desktop configurations; a failure there
    /// costs the user a re-login on next launch, which is preferable to crashing.
    /// </summary>
    private static async Task SaveRefreshTokenAsync(string? token)
    {
        try
        {
            if (token is null) SecureStorage.Default.Remove(RefreshTokenKey);
            else await SecureStorage.Default.SetAsync(RefreshTokenKey, token);
        }
        catch
        {
            // Ignored — session simply won't persist across launches.
        }
    }

    private static async Task<string?> ReadStoredRefreshTokenAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync(RefreshTokenKey);
        }
        catch
        {
            return null;
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUTrade1.Core;

namespace NUTrade1.Services.Firebase;

/// <summary>Obtains a Google ID token to hand to Firebase's <c>signInWithIdp</c>.</summary>
public interface IGoogleAuthBroker
{
    bool IsConfigured { get; }

    /// <summary>Runs the OAuth flow and returns a Google-issued ID token (a JWT).</summary>
    Task<OperationResult<string>> GetIdTokenAsync(CancellationToken ct = default);
}

/// <summary>
/// OAuth 2.0 Authorization Code with PKCE against Google, in the two shapes the
/// platforms actually support:
///
/// • Desktop (Windows, Mac Catalyst) — the loopback flow. The system browser is opened
///   and a one-shot <see cref="HttpListener"/> on 127.0.0.1 catches the redirect. This is
///   what makes Google sign-in work on the unpackaged Windows head, where MAUI's
///   <c>WebAuthenticator</c> cannot round-trip a custom URI scheme.
///
/// • Mobile (Android, iOS) — <c>WebAuthenticator</c> with the reversed-client-id scheme.
///
/// PKCE does the real work in both: the client secret in an installed app is not secret,
/// so the <c>code_verifier</c> is what stops an intercepted authorization code being
/// redeemed by anything but this process.
/// </summary>
public sealed class GoogleAuthBroker : IGoogleAuthBroker
{
    private readonly HttpClient _http;

    public GoogleAuthBroker(HttpClient http) => _http = http;

    public bool IsConfigured => GoogleOAuth.IsConfigured;

    public async Task<OperationResult<string>> GetIdTokenAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return OperationResult<string>.Fail(
                "Google sign-in isn't set up yet. Use your email and password for now.");
        }

        var verifier = CreateCodeVerifier();
        var challenge = CreateCodeChallenge(verifier);
        var state = CreateCodeVerifier();

        try
        {
#if ANDROID || IOS
            return await RunMobileFlowAsync(verifier, challenge, state, ct);
#else
            return await RunLoopbackFlowAsync(verifier, challenge, state, ct);
#endif
        }
        catch (TaskCanceledException)
        {
            return OperationResult<string>.Fail("Google sign-in was cancelled.");
        }
        catch (HttpRequestException)
        {
            return OperationResult<string>.Fail("Can't reach Google right now. Check your connection.");
        }
    }

#if ANDROID || IOS
    private async Task<OperationResult<string>> RunMobileFlowAsync(
        string verifier, string challenge, string state, CancellationToken ct)
    {
        var redirectUri = GoogleOAuth.MobileRedirectUri!;
        var authorizeUrl = BuildAuthorizeUrl(redirectUri, challenge, state);

        WebAuthenticatorResult result;
        try
        {
            result = await WebAuthenticator.Default.AuthenticateAsync(
                new Uri(authorizeUrl), new Uri(redirectUri));
        }
        catch (TaskCanceledException)
        {
            return OperationResult<string>.Fail("Google sign-in was cancelled.");
        }

        if (!result.Properties.TryGetValue("state", out var returnedState) || returnedState != state)
            return OperationResult<string>.Fail("Google sign-in failed a security check. Try again.");

        if (!result.Properties.TryGetValue("code", out var code))
            return OperationResult<string>.Fail("Google didn't return a sign-in code.");

        return await ExchangeCodeAsync(code, verifier, redirectUri, ct);
    }
#else
    private async Task<OperationResult<string>> RunLoopbackFlowAsync(
        string verifier, string challenge, string state, CancellationToken ct)
    {
        // Port 0 lets the OS pick a free one; localhost needs no URL ACL on Windows,
        // so this works without elevation.
        var listenerPort = GetFreePort();
        var redirectUri = $"http://127.0.0.1:{listenerPort}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        try
        {
            await Launcher.Default.OpenAsync(new Uri(BuildAuthorizeUrl(redirectUri, challenge, state)));

            // Don't leave a listener and a browser tab open forever if the user wanders off.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));

            var contextTask = listener.GetContextAsync();
            var completed = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, timeout.Token));
            if (completed != contextTask)
                return OperationResult<string>.Fail("Google sign-in timed out.");

            var context = await contextTask;
            var query = context.Request.QueryString;

            await RespondAsync(context, query["error"] is null);

            if (query["error"] is { } error)
                return OperationResult<string>.Fail($"Google sign-in was declined ({error}).");
            if (query["state"] != state)
                return OperationResult<string>.Fail("Google sign-in failed a security check. Try again.");
            if (query["code"] is not { } code)
                return OperationResult<string>.Fail("Google didn't return a sign-in code.");

            return await ExchangeCodeAsync(code, verifier, redirectUri, ct);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static int GetFreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <summary>Writes the page the browser lands on, so the user isn't left staring at a blank tab.</summary>
    private static async Task RespondAsync(HttpListenerContext context, bool succeeded)
    {
        var message = succeeded
            ? "You're signed in. You can close this tab and go back to NUTrade."
            : "Sign-in was cancelled. You can close this tab.";

        var html = Encoding.UTF8.GetBytes($"""
            <!doctype html><html><head><meta charset="utf-8"><title>NUTrade</title></head>
            <body style="margin:0;display:grid;place-items:center;height:100vh;background:#F4F6FB;
                         font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#1C2C58;">
              <p style="font-size:16px;">{message}</p>
            </body></html>
            """);

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = html.Length;
        await context.Response.OutputStream.WriteAsync(html);
        context.Response.Close();
    }
#endif

    private static string BuildAuthorizeUrl(string redirectUri, string challenge, string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = GoogleOAuth.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = GoogleOAuth.Scopes,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            // Without this Google skips the account chooser when one session is already
            // live, which defeats the point of "use your saved Google account".
            ["prompt"] = "select_account",
        };

        var encoded = string.Join("&", query.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return $"{GoogleOAuth.AuthorizationEndpoint}?{encoded}";
    }

    /// <summary>Redeems the authorization code for the <c>id_token</c> Firebase needs.</summary>
    private async Task<OperationResult<string>> ExchangeCodeAsync(
        string code, string verifier, string redirectUri, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = GoogleOAuth.ClientId,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verifier,
        };

#if !ANDROID && !IOS
        // Google's desktop client type still wants a secret on the token request. It is
        // shipped inside the binary and Google treats it as non-confidential; PKCE is the
        // control that actually matters here.
        if (!string.IsNullOrEmpty(GoogleOAuth.DesktopClientSecret))
            form["client_secret"] = GoogleOAuth.DesktopClientSecret;
#endif

        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(GoogleOAuth.TokenEndpoint, content, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var detail = TryReadError(raw);
            return OperationResult<string>.Fail(
                detail is null ? "Google refused the sign-in. Try again." : $"Google refused the sign-in ({detail}).");
        }

        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.TryGetProperty("id_token", out var idToken) && idToken.GetString() is { } token
            ? OperationResult<string>.Ok(token)
            : OperationResult<string>.Fail("Google didn't return an identity token.");
    }

    private static string? TryReadError(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.TryGetProperty("error_description", out var description)
                ? description.GetString()
                : doc.RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A high-entropy random string, base64url-encoded without padding (RFC 7636).</summary>
    private static string CreateCodeVerifier() =>
        Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string CreateCodeChallenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

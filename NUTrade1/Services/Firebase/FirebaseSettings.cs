namespace NUTrade1.Services.Firebase;

/// <summary>
/// Client configuration for the <c>nutrade-a25c7</c> Firebase project.
///
/// None of these are secrets. A Firebase Web API key is a public project
/// identifier — it authorises nothing on its own, and every read and write it
/// fronts is gated by <c>firestore.rules</c> and the signed-in user's ID token.
/// It is the same material Firebase ships inside <c>google-services.json</c>.
/// The values that <em>are</em> secret (the PayMongo keys, the admin bootstrap
/// secret) live in Secret Manager and are only ever read by Cloud Functions.
/// </summary>
public static class FirebaseSettings
{
    public const string ProjectId = "nutrade-a25c7";
    public const string ApiKey = "AIzaSyDXWnLMuCkcnVUeNRnJLmeRVVXDZ7KXWXo";
    public const string StorageBucket = "nutrade-a25c7.firebasestorage.app";

    /// <summary>Region every Cloud Function in <c>functions/</c> deploys to.</summary>
    public const string FunctionsRegion = "asia-southeast1";

    /// <summary>Root of the Firestore REST surface, below which document paths are appended.</summary>
    public const string FirestoreDocuments =
        $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents";

    public const string IdentityToolkit = "https://identitytoolkit.googleapis.com/v1/accounts";

    public const string SecureTokenEndpoint = "https://securetoken.googleapis.com/v1/token";

    public static string FunctionUrl(string name) =>
        $"https://{FunctionsRegion}-{ProjectId}.cloudfunctions.net/{name}";

    public static string StorageObjectUrl(string objectPath) =>
        $"https://firebasestorage.googleapis.com/v0/b/{StorageBucket}/o/{Uri.EscapeDataString(objectPath)}";

    /// <summary>
    /// How often a page-owned observer re-reads a document it is watching. The REST
    /// transport has no snapshot listener, so <c>ObserveListing</c> / <c>ObserveMessages</c>
    /// poll — short enough that a competing bid or an incoming message shows up while
    /// you are looking at the screen, long enough not to burn quota.
    /// </summary>
    public static readonly TimeSpan ObservePollInterval = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Google OAuth client configuration for "Continue with Google".
///
/// These are per-platform and are NOT created by enabling the Google provider in the
/// Firebase console alone — the console makes a Web client for the JS SDK, which is not
/// what a native app uses. Create the clients under
/// APIs &amp; Services → Credentials in the Google Cloud console for project
/// <c>nutrade-a25c7</c> and paste the ids here.
///
/// Like the Firebase API key these are public identifiers, not secrets: the desktop
/// client secret in particular is explicitly non-confidential in Google's installed-app
/// flow, which is why PKCE carries the actual security.
///
/// <para>
/// <b>The desktop (Windows / Mac Catalyst) flow needs nothing but the id below</b> — it
/// catches the redirect on a loopback socket. The mobile flows additionally need the
/// redirect scheme registered with the OS, which cannot be added until the client ids
/// exist because the scheme is derived from them:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Android</b> — add a <c>WebAuthenticatorCallbackActivity</c> subclass under
/// <c>Platforms/Android/</c> carrying
/// <c>[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "&lt;reversed client id&gt;")]</c>.
/// <c>DataScheme</c> must be a compile-time constant, so paste the reversed id literally.
/// </description></item>
/// <item><description>
/// <b>iOS</b> — add the same reversed id to <c>CFBundleURLSchemes</c> in
/// <c>Platforms/iOS/Info.plist</c>.
/// </description></item>
/// </list>
/// <para>
/// Until an id is filled in for the running platform, <see cref="IsConfigured"/> is false
/// and the app hides the Google button rather than offering a flow that cannot complete.
/// </para>
/// </summary>
public static class GoogleOAuth
{
    /// <summary>"Desktop app" client — used by the loopback flow on Windows and Mac Catalyst.</summary>
    public const string DesktopClientId = "";
    public const string DesktopClientSecret = "";

    /// <summary>"Android" client — needs the package name and your signing SHA-1.</summary>
    public const string AndroidClientId = "";

    /// <summary>"iOS" client — needs the bundle id.</summary>
    public const string IosClientId = "";

    public const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    /// <summary>`openid` is the one that matters — it is what makes Google return an id_token.</summary>
    public const string Scopes = "openid email profile";

    public static bool IsConfigured =>
#if ANDROID
        !string.IsNullOrEmpty(AndroidClientId);
#elif IOS
        !string.IsNullOrEmpty(IosClientId);
#else
        !string.IsNullOrEmpty(DesktopClientId);
#endif

    public static string ClientId =>
#if ANDROID
        AndroidClientId;
#elif IOS
        IosClientId;
#else
        DesktopClientId;
#endif

    /// <summary>
    /// Mobile clients redirect to their own reversed client id; the desktop flow uses a
    /// loopback address chosen at runtime, so it supplies its own.
    /// </summary>
    public static string? MobileRedirectUri =>
#if ANDROID || IOS
        $"{string.Join(".", ClientId.Split('.').Reverse())}:/oauth2redirect";
#else
        null;
#endif
}

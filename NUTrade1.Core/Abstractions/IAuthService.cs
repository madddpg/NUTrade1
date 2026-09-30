namespace NUTrade1.Core;

/// <summary>
/// Firebase Authentication session lifecycle. Students register with any personal
/// email and a password — there is no campus-domain restriction. Two independent
/// gates follow: a one-time code emailed to that address proves they own it, and
/// <see cref="IsVerified"/> reflects the <c>verified</c> custom claim, which is what
/// actually unlocks posting and bidding.
/// </summary>
public interface IAuthService
{
    /// <summary>The signed-in user's UID, or null when signed out.</summary>
    string? CurrentUid { get; }

    /// <summary>The signed-in user's email, or null when signed out.</summary>
    string? CurrentEmail { get; }

    bool IsSignedIn { get; }

    /// <summary>
    /// True once the <c>verified</c> custom claim is present on the ID token. Until then
    /// Firestore rules and the Cloud Functions reject listing creation and bidding.
    /// </summary>
    bool IsVerified { get; }

    /// <summary>
    /// True when the ID token carries <c>role: admin</c>. Only decides whether the app
    /// shows admin screens — every admin action is re-checked by its Cloud Function.
    /// </summary>
    bool IsAdmin { get; }

    /// <summary>Raised after a sign-in or sign-out so the Shell can re-evaluate the auth gate.</summary>
    event EventHandler? AuthStateChanged;

    /// <summary>Restores a persisted session on app start. Safe to call when already signed in.</summary>
    Task RestoreSessionAsync(CancellationToken ct = default);

    /// <summary>Signs in an existing account.</summary>
    Task<OperationResult> SignInWithEmailAsync(string email, string password, CancellationToken ct = default);

    /// <summary>
    /// Whether this build can offer "Continue with Google". False when no Google OAuth
    /// client is configured for the running platform, so the button can be hidden rather
    /// than failing when tapped.
    /// </summary>
    bool IsGoogleSignInAvailable { get; }

    /// <summary>
    /// Signs in with a Google account, creating the NUTrade account on first use.
    /// Google having verified the address does not skip the NUTrade code — the
    /// <c>verified</c> claim is still granted only by <see cref="IEmailVerificationService"/>.
    /// </summary>
    Task<OperationResult> SignInWithGoogleAsync(CancellationToken ct = default);

    /// <summary>Creates a new account for any valid email address.</summary>
    Task<OperationResult> SignUpWithEmailAsync(string email, string password, CancellationToken ct = default);

    /// <summary>
    /// Re-reads the ID token so a claim granted since sign-in — typically <c>verified</c>,
    /// after an admin approves the account — takes effect without making the student sign
    /// out and back in.
    /// </summary>
    /// <param name="force">
    /// Skips the short freshness window and always goes to the network. Screens that poll
    /// this on every appearance leave it false; a caller that has just changed the claims
    /// server-side and needs the new value immediately passes true.
    /// </param>
    Task RefreshClaimsAsync(CancellationToken ct = default, bool force = false);

    Task SignOutAsync(CancellationToken ct = default);
}

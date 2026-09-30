namespace NUTrade1.Services.Firebase;

/// <summary>
/// Hands out a currently-valid Firebase ID token for the signed-in user, refreshing
/// it when it is close to expiry. Every other Firebase-backed service depends on this
/// rather than on <see cref="Core.IAuthService"/> so that the token plumbing stays
/// inside the Firebase implementation and never leaks into Core.
/// </summary>
public interface IFirebaseTokenProvider
{
    /// <summary>
    /// A valid ID token, or null when nobody is signed in. Callers put this in an
    /// <c>Authorization: Bearer</c> header.
    /// </summary>
    Task<string?> GetIdTokenAsync(CancellationToken ct = default);

    /// <summary>UID of the signed-in user, or null.</summary>
    string? CurrentUid { get; }
}

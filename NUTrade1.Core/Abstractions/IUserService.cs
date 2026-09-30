namespace NUTrade1.Core;

/// <summary>Reads and writes <c>users/{uid}</c> profile documents.</summary>
public interface IUserService
{
    /// <summary>Returns the profile for <paramref name="uid"/>, or null when none exists yet.</summary>
    Task<UserProfile?> GetProfileAsync(string uid, CancellationToken ct = default);

    /// <summary>Convenience wrapper for the signed-in user's profile.</summary>
    Task<UserProfile?> GetCurrentProfileAsync(CancellationToken ct = default);

    /// <summary>Creates the signed-in user's profile doc. Rejected server-side if the email is off-domain.</summary>
    Task<OperationResult> CreateProfileAsync(UserProfile profile, CancellationToken ct = default);

    Task<OperationResult> UpdateProfileAsync(UserProfile profile, CancellationToken ct = default);

    /// <summary>
    /// Sets the signed-in user's program to one of <see cref="NUTradeConstants.Programs"/>.
    /// Writes that one field only, so it can't clobber anything else on the profile.
    /// </summary>
    Task<OperationResult> SetProgramAsync(string program, CancellationToken ct = default);
}

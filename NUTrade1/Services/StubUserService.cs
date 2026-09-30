using NUTrade1.Core;

namespace NUTrade1.Services;

/// <summary>
/// Offline stand-in for <see cref="IUserService"/>. Starts empty, so the offline run
/// goes through profile setup exactly as a real first sign-in does.
/// </summary>
public sealed class StubUserService : IUserService
{
    private readonly IAuthService _auth;
    private readonly Dictionary<string, UserProfile> _profiles = new();

    public StubUserService(IAuthService auth) => _auth = auth;

    public Task<UserProfile?> GetProfileAsync(string uid, CancellationToken ct = default) =>
        Task.FromResult(_profiles.TryGetValue(uid, out var p) ? p : null);

    public Task<UserProfile?> GetCurrentProfileAsync(CancellationToken ct = default) =>
        GetProfileAsync(_auth.CurrentUid ?? string.Empty, ct);

    public Task<OperationResult> CreateProfileAsync(UserProfile profile, CancellationToken ct = default)
    {
        if (!NUTradeConstants.IsValidEmail(profile.Email))
            return Task.FromResult(OperationResult.Fail("Enter a valid email address."));

        profile.Uid = _auth.CurrentUid ?? profile.Uid;
        _profiles[profile.Uid] = profile;
        return Task.FromResult(OperationResult.Ok());
    }

    /// <summary>Stands in for completeSignup writing the new account's profile.</summary>
    internal void Seed(UserProfile profile) => _profiles[profile.Uid] = profile;

    public Task<OperationResult> UpdateProfileAsync(UserProfile profile, CancellationToken ct = default)
    {
        _profiles[profile.Uid] = profile;
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> RegisterPushTokenAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return Task.FromResult(OperationResult.Ok());
        if (_auth.CurrentUid is { } uid && _profiles.TryGetValue(uid, out var profile) && !profile.FcmTokens.Contains(token))
            profile.FcmTokens.Add(token);
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> SetProgramAsync(string program, CancellationToken ct = default)
    {
        if (_auth.CurrentUid is { } uid && _profiles.TryGetValue(uid, out var profile))
            profile.Program = program;
        return Task.FromResult(OperationResult.Ok());
    }
}

using System.Collections.Concurrent;
using System.Text.Json;
using NUTrade1.Core;

namespace NUTrade1.Services.Firebase;

/// <summary>
/// <see cref="IUserService"/> over <c>users/{uid}</c>.
///
/// Note what this class deliberately cannot do: <c>verificationStatus</c>, <c>role</c>,
/// <c>tradesCompleted</c> and <c>rating</c> are all rejected by firestore.rules on a
/// client update, so <see cref="UpdateProfileAsync"/> patches only the presentational
/// fields. Verification is granted by <c>setUserVerification</c> after an admin reviews
/// the Student ID photo.
/// </summary>
public sealed class FirestoreUserService : IUserService
{
    /// <summary>Fields a student is allowed to change about themselves.</summary>
    private static readonly string[] SelfEditableFields =
        { "displayName", "studentId", "campusHub", "photoUrl", "fcmTokens", "program" };

    private readonly FirestoreClient _firestore;
    private readonly IAuthService _auth;

    // Seller rows on the feed and detail pages would otherwise re-read the same few
    // profiles constantly; profiles change rarely, so hold them for the session.
    private readonly ConcurrentDictionary<string, UserProfile> _cache = new();

    public FirestoreUserService(FirestoreClient firestore, IAuthService auth)
    {
        _firestore = firestore;
        _auth = auth;
    }

    public async Task<UserProfile?> GetProfileAsync(string uid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uid)) return null;
        if (_cache.TryGetValue(uid, out var cached)) return cached;

        var document = await _firestore.GetDocumentAsync($"users/{uid}", ct);
        if (document is not { } doc) return null;

        var profile = Map(doc);
        _cache[uid] = profile;
        return profile;
    }

    public Task<UserProfile?> GetCurrentProfileAsync(CancellationToken ct = default) =>
        _auth.CurrentUid is { } uid ? GetProfileAsync(uid, ct) : Task.FromResult<UserProfile?>(null);

    public async Task<OperationResult> CreateProfileAsync(UserProfile profile, CancellationToken ct = default)
    {
        if (_auth.CurrentUid is not { } uid) return OperationResult.Fail("Sign in to continue.");
        if (!NUTradeConstants.IsValidEmail(profile.Email))
            return OperationResult.Fail("Enter a valid email address.");

        profile.Uid = uid;

        var fields = new Dictionary<string, object?>
        {
            ["uid"] = Fs.Str(uid),
            ["email"] = Fs.Str(profile.Email),
            ["displayName"] = Fs.Str(profile.DisplayName),
            ["firstName"] = Fs.Str(profile.FirstName),
            ["lastName"] = Fs.Str(profile.LastName),
            ["studentId"] = Fs.Str(profile.StudentId),
            ["campusHub"] = Fs.Enum(profile.CampusHub),
            ["photoUrl"] = Fs.Str(profile.PhotoUrl),
            ["tradesCompleted"] = Fs.Int(0),
            ["rating"] = Fs.Null(),
            // The two fields firestore.rules pins on create: a new account is an
            // unprivileged student awaiting ID review, whatever the client claims.
            ["role"] = Fs.Str("student"),
            ["verificationStatus"] = Fs.Str("pending"),
            ["program"] = Fs.Str(profile.Program),
            ["fcmTokens"] = Fs.Arr(Array.Empty<string>()),
            ["createdAt"] = Fs.Ts(DateTimeOffset.UtcNow),
        };

        try
        {
            await _firestore.CreateDocumentAsync("users", uid, fields, ct);
            _cache.TryRemove(uid, out _);
            return OperationResult.Ok();
        }
        catch (FirestoreException ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    public async Task<OperationResult> UpdateProfileAsync(UserProfile profile, CancellationToken ct = default)
    {
        if (_auth.CurrentUid is not { } uid) return OperationResult.Fail("Sign in to continue.");

        var fields = new Dictionary<string, object?>
        {
            ["displayName"] = Fs.Str(profile.DisplayName),
            ["studentId"] = Fs.Str(profile.StudentId),
            ["campusHub"] = Fs.Enum(profile.CampusHub),
            ["photoUrl"] = Fs.Str(profile.PhotoUrl),
            ["fcmTokens"] = Fs.Arr(profile.FcmTokens),
            ["program"] = Fs.Str(profile.Program),
        };

        // Belt and braces: the rules enforce this too, but failing here gives a clearer
        // error than a 403 if someone adds a field to the dictionary above.
        var disallowed = fields.Keys.Except(SelfEditableFields).ToArray();
        if (disallowed.Length > 0)
            return OperationResult.Fail($"Can't change {string.Join(", ", disallowed)} from the app.");

        try
        {
            await _firestore.PatchAsync($"users/{uid}", fields, ct);
            _cache.TryRemove(uid, out _);
            return OperationResult.Ok();
        }
        catch (FirestoreException ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    public async Task<OperationResult> SetProgramAsync(string program, CancellationToken ct = default)
    {
        if (_auth.CurrentUid is not { } uid) return OperationResult.Fail("Sign in to continue.");
        if (!NUTradeConstants.Programs.Contains(program)) return OperationResult.Fail("Choose your program.");

        try
        {
            await _firestore.PatchAsync($"users/{uid}", new Dictionary<string, object?> { ["program"] = Fs.Str(program) }, ct);
            _cache.TryRemove(uid, out _);
            return OperationResult.Ok();
        }
        catch (FirestoreException ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    internal static UserProfile Map(JsonElement document)
    {
        var fields = document.GetProperty("fields");
        return new UserProfile
        {
            Uid = Fs.StringOr(fields, "uid", Fs.IdFromName(document)),
            DisplayName = Fs.StringOr(fields, "displayName"),
            FirstName = Fs.StringOr(fields, "firstName"),
            LastName = Fs.StringOr(fields, "lastName"),
            StudentId = Fs.StringOr(fields, "studentId"),
            Email = Fs.StringOr(fields, "email"),
            CampusHub = Fs.Enum(fields, "campusHub", CampusHub.Unknown),
            PhotoUrl = Fs.String(fields, "photoUrl"),
            TradesCompleted = Fs.Int32(fields, "tradesCompleted"),
            Rating = Fs.DoubleOrNull(fields, "rating"),
            CreatedAt = Fs.Timestamp(fields, "createdAt") ?? DateTimeOffset.UtcNow,
            Program = Fs.StringOr(fields, "program"),
            FcmTokens = Fs.StringList(fields, "fcmTokens"),
        };
    }
}

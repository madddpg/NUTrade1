using System.Text.Json;
using System.Text.Json.Serialization;

namespace NUTrade1.Core;

/// <summary>
/// How long a saved feed or profile may stand in for a network read. Pull-to-refresh
/// still fetches immediately; this only decides whether a cold start has to.
/// </summary>
public static class LocalCachePolicy
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(60);

    public static bool IsFresh(DateTimeOffset savedAt, DateTimeOffset now) =>
        savedAt <= now && now - savedAt < MaxAge;
}

/// <summary>The feed page last saved for one category filter, including "all".</summary>
public sealed class FeedSnapshot
{
    public DateTimeOffset SavedAt { get; set; }

    /// <summary>The account this page was saved for. A later sign-in must not be shown it.</summary>
    public string OwnerUid { get; set; } = string.Empty;

    /// <summary>Empty for the unfiltered feed; otherwise the <see cref="ItemCategory"/> name.</summary>
    public string CategoryKey { get; set; } = string.Empty;

    public string? NextCursor { get; set; }

    public List<CachedListing> Items { get; set; } = new();
}

/// <summary>The signed-in student's profile, saved so Profile can paint before the network answers.</summary>
public sealed class ProfileSnapshot
{
    public DateTimeOffset SavedAt { get; set; }

    public UserProfile Profile { get; set; } = new();
}

/// <summary>
/// On-device copies of the feed and the current profile. Cache-first on a cold start;
/// a snapshot older than <see cref="LocalCachePolicy.MaxAge"/> is shown and then replaced.
/// </summary>
public interface ILocalCache
{
    Task<FeedSnapshot?> ReadFeedAsync(string uid, string categoryKey, CancellationToken ct = default);

    Task WriteFeedAsync(FeedSnapshot snapshot, CancellationToken ct = default);

    Task<ProfileSnapshot?> ReadProfileAsync(string uid, CancellationToken ct = default);

    Task WriteProfileAsync(ProfileSnapshot snapshot, CancellationToken ct = default);
}

/// <summary>
/// JSON files under a directory the app owns. One file per feed filter and one per user,
/// so a category change does not wipe the unfiltered feed.
/// </summary>
public sealed class JsonFileCache : ILocalCache
{
    private readonly string _directory;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public JsonFileCache(string directory) => _directory = directory;

    public Task<FeedSnapshot?> ReadFeedAsync(string uid, string categoryKey, CancellationToken ct = default) =>
        ReadAsync<FeedSnapshot>(FeedPath(uid, categoryKey), ct);

    public Task WriteFeedAsync(FeedSnapshot snapshot, CancellationToken ct = default) =>
        WriteAsync(FeedPath(snapshot.OwnerUid, snapshot.CategoryKey), snapshot, ct);

    public Task<ProfileSnapshot?> ReadProfileAsync(string uid, CancellationToken ct = default) =>
        ReadAsync<ProfileSnapshot>(ProfilePath(uid), ct);

    public Task WriteProfileAsync(ProfileSnapshot snapshot, CancellationToken ct = default) =>
        WriteAsync(ProfilePath(snapshot.Profile.Uid), snapshot, ct);

    private string FeedPath(string uid, string categoryKey) =>
        Path.Combine(
            _directory,
            $"feed-{Sanitize(uid)}-{(string.IsNullOrEmpty(categoryKey) ? "all" : Sanitize(categoryKey))}.json");

    private string ProfilePath(string uid) =>
        Path.Combine(_directory, $"profile-{Sanitize(uid)}.json");

    private static string Sanitize(string value)
    {
        var safe = new string(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return safe.Length == 0 ? "unknown" : safe;
    }

    private async Task<T?> ReadAsync<T>(string path, CancellationToken ct) where T : class
    {
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Json, ct);
    }

    private async Task WriteAsync<T>(string path, T value, CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, value, Json, ct);
        }

        File.Move(temp, path, overwrite: true);
    }
}

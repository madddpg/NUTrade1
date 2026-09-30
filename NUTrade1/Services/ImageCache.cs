using Microsoft.Maui.Graphics.Platform;

namespace NUTrade1.Services;

/// <summary>
/// Downsamples and re-encodes a picked photo into the app's cache directory before it's
/// attached to a draft listing, so a multi-megabyte camera-resolution photo doesn't sit on
/// disk untouched — a 1280px-max JPEG at 80% quality is typically an order of magnitude smaller.
///
/// The cache is bounded: copies are dropped as soon as a draft is posted or a photo removed,
/// and <see cref="TrimAsync"/> sweeps whatever an interrupted post left behind, so the folder
/// can never grow without limit on a student's phone.
/// </summary>
public static class ImageCache
{
    private const float MaxDimension = 1280f;
    private const float JpegQuality = 0.8f;
    private const string CacheFolderName = "listing-photos";

    /// <summary>Nothing here is needed once a draft is posted, so the sweep is aggressive.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    /// <summary>At ~200 KB a photo this is a hundred or so, far more than a draft ever holds.</summary>
    private const long MaxCacheBytes = 20 * 1024 * 1024;

    public static Task<string> CacheAsync(string sourcePath, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var cacheDir = CacheDirectory();
            var destPath = Path.Combine(cacheDir, $"{Guid.NewGuid():N}.jpg");

            try
            {
                using var sourceStream = File.OpenRead(sourcePath);
                using var image = PlatformImage.FromStream(sourceStream);

                var scale = Math.Min(1f, MaxDimension / Math.Max(image.Width, image.Height));
                using var final = scale < 1f
                    ? image.Resize(image.Width * scale, image.Height * scale, ResizeMode.Fit, disposeOriginal: false)
                    : image;

                using var destStream = File.Create(destPath);
                final.Save(destStream, ImageFormat.Jpeg, JpegQuality);
            }
            catch
            {
                // Platform image decoding isn't guaranteed everywhere; fall back to the
                // original file rather than losing the photo the user just picked.
                File.Copy(sourcePath, destPath, overwrite: true);
            }

            return destPath;
        }, ct);

    /// <summary>Best-effort cleanup when a photo is removed from a draft, or once it's uploaded.</summary>
    public static void Delete(string cachedPath)
    {
        try
        {
            if (File.Exists(cachedPath)) File.Delete(cachedPath);
        }
        catch
        {
            // Not worth surfacing to the user; TrimAsync will catch it later.
        }
    }

    public static void Delete(IEnumerable<string> cachedPaths)
    {
        foreach (var path in cachedPaths) Delete(path);
    }

    /// <summary>
    /// Drops cached photos older than a day, then the oldest of whatever is left until the
    /// folder is under the size cap. Runs at startup; failures are ignored on purpose.
    /// </summary>
    public static Task TrimAsync() => Task.Run(() =>
    {
        try
        {
            var files = new DirectoryInfo(CacheDirectory())
                .GetFiles("*.jpg")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            var cutoff = DateTime.UtcNow - MaxAge;
            long kept = 0;

            foreach (var file in files)
            {
                var tooOld = file.LastWriteTimeUtc < cutoff;
                var overCap = kept + file.Length > MaxCacheBytes;

                if (tooOld || overCap)
                {
                    try { file.Delete(); } catch { /* in use, or already gone */ }
                }
                else
                {
                    kept += file.Length;
                }
            }
        }
        catch
        {
            // No cache directory yet, or the OS cleared it — nothing to do either way.
        }
    });

    private static string CacheDirectory()
    {
        var dir = Path.Combine(FileSystem.CacheDirectory, CacheFolderName);
        Directory.CreateDirectory(dir);
        return dir;
    }
}

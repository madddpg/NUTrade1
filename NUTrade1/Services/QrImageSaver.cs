using NUTrade1.Core;

namespace NUTrade1.Services;

/// <summary>
/// Gets a payment QR off the screen and into the student's hands, so they can pay from the
/// same phone that shows it: GCash, Maya and most bank apps have an "upload QR" option that
/// reads a picture from the gallery.
/// </summary>
public interface IQrImageSaver
{
    /// <summary>
    /// Writes the PNG where the platform's photo pickers look — Photos on iOS, the gallery
    /// (Pictures/NUTrade) on Android, Pictures\NUTrade on Windows. On success the value is
    /// where it went, in words for a toast.
    /// </summary>
    Task<OperationResult<string>> SaveToGalleryAsync(byte[] png, string fileName, CancellationToken ct = default);

    /// <summary>Opens the share sheet with the QR attached, so it can go straight to a payment app.</summary>
    Task<OperationResult> ShareAsync(byte[] png, string fileName, string title, CancellationToken ct = default);
}

public sealed partial class QrImageSaver : IQrImageSaver
{
    private const string AlbumName = "NUTrade";
    private const string MimeType = "image/png";

    public async Task<OperationResult<string>> SaveToGalleryAsync(byte[] png, string fileName, CancellationToken ct = default)
    {
        try
        {
            return await SavePlatformAsync(png, SafeFileName(fileName), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"QrImageSaver.SaveToGalleryAsync: {ex}");
            return OperationResult<string>.Fail("Couldn't save the QR code. Try Share instead.");
        }
    }

    public async Task<OperationResult> ShareAsync(byte[] png, string fileName, string title, CancellationToken ct = default)
    {
        try
        {
            // The share sheet needs a real file; the cache is fine, the OS copies it on send.
            var path = Path.Combine(FileSystem.CacheDirectory, SafeFileName(fileName));
            await File.WriteAllBytesAsync(path, png, ct);
            await MainThread.InvokeOnMainThreadAsync(() =>
                Share.Default.RequestAsync(new ShareFileRequest { Title = title, File = new ShareFile(path, MimeType) }));
            return OperationResult.Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"QrImageSaver.ShareAsync: {ex}");
            return OperationResult.Fail("Couldn't open the share sheet.");
        }
    }

    /// <summary>A name every platform's file system accepts, always ending in .png.</summary>
    private static string SafeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(fileName.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = $"nutrade-qr-{DateTime.Now:yyyyMMdd-HHmmss}";
        return cleaned.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? cleaned : cleaned + ".png";
    }

#if ANDROID
    private static async Task<OperationResult<string>> SavePlatformAsync(byte[] png, string fileName, CancellationToken ct)
    {
        var context = Platform.AppContext;
        var resolver = context.ContentResolver
            ?? throw new InvalidOperationException("No content resolver.");

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            // Scoped storage: MediaStore needs no permission for the app's own pictures.
            // IS_PENDING hides the entry until the bytes are all written, so a gallery
            // never shows a half-saved image.
            var values = new Android.Content.ContentValues();
            values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
            values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, MimeType);
            values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath,
                $"{Android.OS.Environment.DirectoryPictures}/{AlbumName}");
            values.Put(Android.Provider.MediaStore.IMediaColumns.IsPending, 1);

            var uri = resolver.Insert(Android.Provider.MediaStore.Images.Media.ExternalContentUri!, values)
                ?? throw new InvalidOperationException("MediaStore refused the insert.");
            try
            {
                await using (var stream = resolver.OpenOutputStream(uri)
                    ?? throw new InvalidOperationException("No output stream."))
                {
                    await stream.WriteAsync(png, ct);
                }

                values.Clear();
                values.Put(Android.Provider.MediaStore.IMediaColumns.IsPending, 0);
                resolver.Update(uri, values, null, null);
            }
            catch
            {
                resolver.Delete(uri, null, null);
                throw;
            }

            return OperationResult<string>.Ok($"your gallery (Pictures/{AlbumName})");
        }

        // Android 9 and older: the public Pictures folder, behind the storage permission
        // (declared with maxSdkVersion 28 in the manifest, so newer phones never ask).
        var status = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.StorageWrite>);
        if (status != PermissionStatus.Granted)
            return OperationResult<string>.Fail("Allow storage access to save the QR code, or use Share.");

        var pictures = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryPictures)!;
        var dir = Path.Combine(pictures.AbsolutePath, AlbumName);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        await File.WriteAllBytesAsync(path, png, ct);
        Android.Media.MediaScannerConnection.ScanFile(context, [path], [MimeType], null);

        return OperationResult<string>.Ok($"your gallery (Pictures/{AlbumName})");
    }
#elif IOS || MACCATALYST
    private static async Task<OperationResult<string>> SavePlatformAsync(byte[] png, string fileName, CancellationToken ct)
    {
        // Add-only access is all saving needs, and is the least a student has to grant.
        var access = await Photos.PHPhotoLibrary.RequestAuthorizationAsync(Photos.PHAccessLevel.AddOnly);
        if (access is not (Photos.PHAuthorizationStatus.Authorized or Photos.PHAuthorizationStatus.Limited))
            return OperationResult<string>.Fail("Allow NUTrade to add photos in Settings to save the QR code, or use Share.");

        var image = UIKit.UIImage.LoadFromData(Foundation.NSData.FromArray(png))
            ?? throw new InvalidOperationException("The QR image couldn't be read.");

        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Photos.PHPhotoLibrary.SharedPhotoLibrary.PerformChanges(
            () => Photos.PHAssetChangeRequest.FromImage(image),
            (ok, error) =>
            {
                if (ok) done.TrySetResult(true);
                else done.TrySetException(new InvalidOperationException(error?.LocalizedDescription ?? "Save failed."));
            });

        await done.Task.WaitAsync(ct);
        return OperationResult<string>.Ok("Photos");
    }
#elif WINDOWS
    private static async Task<OperationResult<string>> SavePlatformAsync(byte[] png, string fileName, CancellationToken ct)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), AlbumName);
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, fileName), png, ct);
        return OperationResult<string>.Ok($@"Pictures\{AlbumName}");
    }
#else
    private static Task<OperationResult<string>> SavePlatformAsync(byte[] png, string fileName, CancellationToken ct) =>
        Task.FromResult(OperationResult<string>.Fail("Saving isn't supported here. Use Share instead."));
#endif
}

using NUTrade1.Core;

namespace NUTrade1.Services;

/// <summary>Puts a receipt somewhere the student can keep, and opens the share sheet.</summary>
public interface IReceiptFile
{
    /// <summary>Saves the receipt. The value is where it went, in words for a toast.</summary>
    Task<OperationResult<string>> SaveAsync(string text, string fileName, CancellationToken ct = default);

    Task<OperationResult> ShareAsync(string text, string fileName, string title, CancellationToken ct = default);
}

public sealed class ReceiptFile : IReceiptFile
{
    public async Task<OperationResult<string>> SaveAsync(string text, string fileName, CancellationToken ct = default)
    {
        try
        {
            var path = await WriteAsync(text, fileName, ct);
#if ANDROID
            return await SaveToDownloadsAsync(path, Path.GetFileName(path));
#else
            await MainThread.InvokeOnMainThreadAsync(() =>
                Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "Save receipt",
                    File = new ShareFile(path, "text/plain"),
                }));
            return OperationResult<string>.Ok("Choose where to save the receipt.");
#endif
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ReceiptFile.SaveAsync: {ex}");
            return OperationResult<string>.Fail("Couldn't save the receipt. Try Share.");
        }
    }

    public async Task<OperationResult> ShareAsync(string text, string fileName, string title, CancellationToken ct = default)
    {
        try
        {
            var path = await WriteAsync(text, fileName, ct);
            await MainThread.InvokeOnMainThreadAsync(() =>
                Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = title,
                    File = new ShareFile(path, "text/plain"),
                }));
            return OperationResult.Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ReceiptFile.ShareAsync: {ex}");
            return OperationResult.Fail("Couldn't open the share sheet.");
        }
    }

    private static async Task<string> WriteAsync(string text, string fileName, CancellationToken ct)
    {
        var safe = fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".txt";
        var path = Path.Combine(FileSystem.CacheDirectory, safe);
        await File.WriteAllTextAsync(path, text, ct);
        return path;
    }

#if ANDROID
    private static async Task<OperationResult<string>> SaveToDownloadsAsync(string path, string fileName)
    {
        var resolver = Platform.AppContext.ContentResolver;
        if (resolver is null) return OperationResult<string>.Fail("Couldn't save the receipt. Try Share.");

        var values = new Android.Content.ContentValues();
        values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, "text/plain");
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Q)
        {
            values.Put(
                Android.Provider.MediaStore.IMediaColumns.RelativePath,
                Android.OS.Environment.DirectoryDownloads + "/NUTrade");
            var uri = resolver.Insert(Android.Provider.MediaStore.Downloads.ExternalContentUri, values);
            if (uri is null) return OperationResult<string>.Fail("Couldn't save the receipt. Try Share.");
            using var output = resolver.OpenOutputStream(uri);
            if (output is null) return OperationResult<string>.Fail("Couldn't save the receipt. Try Share.");
            using var input = File.OpenRead(path);
            await input.CopyToAsync(output);
            return OperationResult<string>.Ok("Saved to Downloads.");
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
            Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Save receipt",
                File = new ShareFile(path, "text/plain"),
            }));
        return OperationResult<string>.Ok("Choose where to save the receipt.");
    }
#endif
}

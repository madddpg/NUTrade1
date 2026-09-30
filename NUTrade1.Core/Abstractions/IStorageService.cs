namespace NUTrade1.Core;

/// <summary>Uploads to Firebase Storage and hands back download URLs.</summary>
public interface IStorageService
{
    /// <summary>Uploads a local file under <paramref name="folder"/> and returns its download URL.</summary>
    Task<OperationResult<string>> UploadAsync(
        string folder,
        string localFilePath,
        CancellationToken ct = default);

    Task<OperationResult> DeleteAsync(string downloadUrl, CancellationToken ct = default);
}

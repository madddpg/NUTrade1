using NUTrade1.Core;

namespace NUTrade1.Services;

/// <summary>Stand-in for <see cref="IStorageService"/>; echoes back a file URI until Phase 3 wires Firebase Storage.</summary>
public sealed class StubStorageService : IStorageService
{
    public Task<OperationResult<string>> UploadAsync(string folder, string localFilePath, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<string>.Ok(new Uri(localFilePath).AbsoluteUri));

    public Task<OperationResult> DeleteAsync(string downloadUrl, CancellationToken ct = default) =>
        Task.FromResult(OperationResult.Ok());
}

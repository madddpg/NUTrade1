using System.Net.Http.Headers;
using System.Text.Json;
using NUTrade1.Core;

namespace NUTrade1.Services.Firebase;

/// <summary>
/// <see cref="IStorageService"/> over the Firebase Storage REST API.
///
/// Uploads land under <c>listings/{uid}/...</c> or <c>ids/{uid}/...</c> and are
/// governed by <c>storage.rules</c>, which is what keeps one student from writing
/// into another's folder or from uploading a 40MB "photo". The returned download URL
/// carries the object's own access token, which is the form the listing document
/// stores and the feed binds to.
/// </summary>
public sealed class FirebaseStorageService : IStorageService
{
    /// <summary>Matches the ceiling enforced in storage.rules.</summary>
    private const long MaxUploadBytes = 8 * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly IFirebaseTokenProvider _tokens;

    public FirebaseStorageService(HttpClient http, IFirebaseTokenProvider tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    public async Task<OperationResult<string>> UploadAsync(
        string folder, string localFilePath, CancellationToken ct = default)
    {
        var token = await _tokens.GetIdTokenAsync(ct);
        if (token is null) return OperationResult<string>.Fail("Sign in to continue.");

        if (!File.Exists(localFilePath))
            return OperationResult<string>.Fail("That photo is no longer on this device.");

        var info = new FileInfo(localFilePath);
        if (info.Length > MaxUploadBytes)
            return OperationResult<string>.Fail("That photo is too large — keep it under 8 MB.");

        var extension = Path.GetExtension(localFilePath);
        var objectPath = $"{folder.Trim('/')}/{Guid.NewGuid():n}{extension}";

        var url = $"https://firebasestorage.googleapis.com/v0/b/{FirebaseSettings.StorageBucket}/o"
                  + $"?uploadType=media&name={Uri.EscapeDataString(objectPath)}";

        try
        {
            await using var stream = File.OpenRead(localFilePath);
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue(ContentTypeFor(extension));

            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return OperationResult<string>.Fail(
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden
                        ? "You don't have permission to upload that yet."
                        : "Could not upload your photo. Try again.");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

            // Firebase mints a per-object download token; the public URL is only usable
            // with it, which is why it has to be read back off the upload response.
            var downloadToken = doc.RootElement.TryGetProperty("downloadTokens", out var t)
                ? t.GetString()
                : null;

            var downloadUrl = FirebaseSettings.StorageObjectUrl(objectPath) + "?alt=media"
                              + (downloadToken is null ? string.Empty : $"&token={downloadToken}");

            return OperationResult<string>.Ok(downloadUrl);
        }
        catch (HttpRequestException)
        {
            return OperationResult<string>.Fail("Can't reach NUTrade right now. Check your connection.");
        }
        catch (IOException)
        {
            return OperationResult<string>.Fail("Could not read that photo from your device.");
        }
    }

    public async Task<OperationResult> DeleteAsync(string downloadUrl, CancellationToken ct = default)
    {
        var token = await _tokens.GetIdTokenAsync(ct);
        if (token is null) return OperationResult.Fail("Sign in to continue.");

        // Strip the query string: the object endpoint is the same URL without ?alt=media.
        var objectUrl = downloadUrl.Split('?')[0];

        using var request = new HttpRequestMessage(HttpMethod.Delete, objectUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _http.SendAsync(request, ct);
            return response.IsSuccessStatusCode
                ? OperationResult.Ok()
                : OperationResult.Fail("Could not delete that photo.");
        }
        catch (HttpRequestException)
        {
            return OperationResult.Fail("Can't reach NUTrade right now.");
        }
    }

    private static string ContentTypeFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".heic" => "image/heic",
        _ => "image/jpeg",
    };
}

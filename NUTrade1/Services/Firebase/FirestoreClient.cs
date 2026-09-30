using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace NUTrade1.Services.Firebase;

/// <summary>Raised when Firestore rejects a read or write; carries a message fit to show a student.</summary>
public sealed class FirestoreException : Exception
{
    public FirestoreException(string message, HttpStatusCode? status = null) : base(message) => Status = status;
    public HttpStatusCode? Status { get; }
}

/// <summary>
/// A thin client over the Firestore REST API, authenticated as the signed-in user.
///
/// Every call carries that user's ID token, so <c>firestore.rules</c> applies exactly
/// as it would to a native SDK — this is a different transport, not a way around the
/// security model. What REST does not give us is a snapshot listener, so the
/// <c>Observe*</c> methods on the services above poll instead.
/// </summary>
public sealed class FirestoreClient
{
    private readonly HttpClient _http;
    private readonly IFirebaseTokenProvider _tokens;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    public FirestoreClient(HttpClient http, IFirebaseTokenProvider tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    /// <summary>Reads one document. Returns null when it does not exist (a 404 is not an error here).</summary>
    public async Task<JsonElement?> GetDocumentAsync(string path, CancellationToken ct = default)
    {
        using var request = await BuildAsync(HttpMethod.Get, $"{FirebaseSettings.FirestoreDocuments}/{path}", null, ct);
        using var response = await _http.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await ThrowIfFailedAsync(response, ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Runs a structured query below <paramref name="parentPath"/> (empty for the database
    /// root) and returns the matching documents. Rows without a <c>document</c> — Firestore
    /// emits those as keep-alives — are skipped.
    /// </summary>
    public async Task<List<JsonElement>> RunQueryAsync(
        string parentPath, object structuredQuery, CancellationToken ct = default)
    {
        var url = string.IsNullOrEmpty(parentPath)
            ? $"{FirebaseSettings.FirestoreDocuments}:runQuery"
            : $"{FirebaseSettings.FirestoreDocuments}/{parentPath}:runQuery";

        var body = new Dictionary<string, object?> { ["structuredQuery"] = structuredQuery };

        using var request = await BuildAsync(HttpMethod.Post, url, body, ct);
        using var response = await _http.SendAsync(request, ct);
        await ThrowIfFailedAsync(response, ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var results = new List<JsonElement>();

        if (doc.RootElement.ValueKind != JsonValueKind.Array) return results;

        foreach (var row in doc.RootElement.EnumerateArray())
        {
            if (row.TryGetProperty("document", out var document))
                results.Add(document.Clone());
        }
        return results;
    }

    /// <summary>Creates a document, letting Firestore allocate the id unless one is supplied.</summary>
    public async Task<string> CreateDocumentAsync(
        string collectionPath,
        string? documentId,
        Dictionary<string, object?> fields,
        CancellationToken ct = default)
    {
        var url = $"{FirebaseSettings.FirestoreDocuments}/{collectionPath}";
        if (!string.IsNullOrEmpty(documentId))
            url += $"?documentId={Uri.EscapeDataString(documentId)}";

        var body = new Dictionary<string, object?> { ["fields"] = fields };

        using var request = await BuildAsync(HttpMethod.Post, url, body, ct);
        using var response = await _http.SendAsync(request, ct);
        await ThrowIfFailedAsync(response, ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return Fs.IdFromName(doc.RootElement);
    }

    /// <summary>
    /// Patches only the named fields. The update mask matters: without it Firestore
    /// treats the write as a full replace and silently drops every field left out.
    /// </summary>
    public async Task PatchAsync(string path, Dictionary<string, object?> fields, CancellationToken ct = default)
    {
        var mask = string.Join("&", fields.Keys.Select(k => $"updateMask.fieldPaths={Uri.EscapeDataString(k)}"));
        var url = $"{FirebaseSettings.FirestoreDocuments}/{path}?{mask}";
        var body = new Dictionary<string, object?> { ["fields"] = fields };

        using var request = await BuildAsync(HttpMethod.Patch, url, body, ct);
        using var response = await _http.SendAsync(request, ct);
        await ThrowIfFailedAsync(response, ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        using var request = await BuildAsync(HttpMethod.Delete, $"{FirebaseSettings.FirestoreDocuments}/{path}", null, ct);
        using var response = await _http.SendAsync(request, ct);
        await ThrowIfFailedAsync(response, ct);
    }

    private async Task<HttpRequestMessage> BuildAsync(
        HttpMethod method, string url, object? body, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, url);

        var token = await _tokens.GetIdTokenAsync(ct);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
            request.Content = JsonContent.Create(body, options: SerializerOptions);

        return request;
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var raw = await response.Content.ReadAsStringAsync(ct);
        var detail = TryReadErrorMessage(raw);

        throw new FirestoreException(
            response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Your session expired. Sign in again.",
                HttpStatusCode.Forbidden =>
                    "You don't have permission for that yet — your student ID may still be awaiting review.",
                HttpStatusCode.NotFound => "That item no longer exists.",
                HttpStatusCode.TooManyRequests => "NUTrade is busy right now. Try again in a moment.",
                _ => string.IsNullOrEmpty(detail) ? "Something went wrong talking to NUTrade." : detail,
            },
            response.StatusCode);
    }

    /// <summary>
    /// Pulls <c>error.message</c> out of a Google API error body. A failed index lookup
    /// reports itself here with a console link to create the index, which is worth
    /// surfacing rather than swallowing.
    /// </summary>
    private static string? TryReadErrorMessage(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            // runQuery returns a single-element array wrapping the error.
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                root = root[0];

            return root.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message)
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

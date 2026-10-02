namespace NUTrade1.Core;

/// <summary>
/// Turns the QR image PayMongo hands back into PNG bytes the app can draw and save.
///
/// PayMongo's QR Ph `code.image_url` is not a hosted file: it is a `data:image/png;base64,…`
/// URI. MAUI reads a string image source as a web address or a file name, so passing that
/// string straight to an Image shows nothing. Decoding it here gives the screen something
/// it can render, and the same bytes are what "Save QR" writes to the gallery.
/// </summary>
public static class QrImageData
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    /// <summary>
    /// The decoded image when either field carries one inline — a data URI or raw
    /// base64 in <paramref name="imageUrl"/>, or bare base64 in <paramref name="imageBase64"/>.
    /// Null when there is nothing inline, including when <paramref name="imageUrl"/> is a
    /// real web address; see <see cref="RemoteUri"/> for that case.
    /// </summary>
    public static byte[]? TryDecode(string? imageUrl, string? imageBase64)
    {
        if (TryDecodeDataUri(imageUrl) is { } fromUri) return fromUri;
        // PayMongo documents image_url as a base64 string. That is sometimes a data
        // URI and sometimes the PNG's raw base64, which is not a web address.
        if (TryDecodeBase64(imageUrl) is { } fromUrl) return fromUrl;
        return TryDecodeBase64(imageBase64);
    }

    /// <summary>The image's web address, when PayMongo sent a hosted image instead of an inline one.</summary>
    public static Uri? RemoteUri(string? imageUrl) =>
        Uri.TryCreate(imageUrl?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : null;

    private static byte[]? TryDecodeDataUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (!text.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;

        var comma = text.IndexOf(',');
        if (comma < 0) return null;

        // Only base64 payloads: a QR is binary, and a percent-encoded PNG is not a thing
        // anyone sends.
        var header = text[..comma];
        if (!header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) return null;

        return TryDecodeBase64(text[(comma + 1)..]);
    }

    private static byte[]? TryDecodeBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var bytes = Convert.FromBase64String(value.Trim());
            return IsImage(bytes) ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// A cheap sanity check on the first few bytes, so a truncated or mislabelled payload
    /// is reported as "no QR" instead of saved to someone's gallery as a broken picture.
    /// </summary>
    private static bool IsImage(byte[] bytes) =>
        bytes.AsSpan().StartsWith(PngSignature) || bytes.AsSpan().StartsWith(JpegSignature);
}

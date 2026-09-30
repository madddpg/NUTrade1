using NUTrade1.Core;
using Xunit;

namespace NUTrade1.Tests;

/// <summary>
/// Reading PayMongo's QR image. Getting this wrong shows an empty box where the code should
/// be, or saves a broken file to the student's gallery — both at the moment they're trying
/// to pay.
/// </summary>
public class QrImageDataTests
{
    // The 8-byte PNG signature plus a little filler: enough for the sanity check.
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    private static readonly string PngBase64 = Convert.ToBase64String(Png);

    [Fact]
    public void Decodes_the_data_uri_PayMongo_sends_as_image_url()
    {
        Assert.Equal(Png, QrImageData.TryDecode($"data:image/png;base64,{PngBase64}", null));
    }

    [Fact]
    public void Tolerates_surrounding_whitespace_and_upper_case_scheme()
    {
        Assert.Equal(Png, QrImageData.TryDecode($"  DATA:image/png;BASE64,{PngBase64}\n", null));
    }

    [Fact]
    public void Falls_back_to_the_bare_base64_field()
    {
        Assert.Equal(Png, QrImageData.TryDecode(null, PngBase64));
        Assert.Equal(Png, QrImageData.TryDecode("https://example.com/qr.png", PngBase64));
    }

    [Fact]
    public void Accepts_a_jpeg_too()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10];
        Assert.Equal(jpeg, QrImageData.TryDecode(null, Convert.ToBase64String(jpeg)));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("https://example.com/qr.png", null)]
    [InlineData("data:image/png;base64,not-base64!!", null)]
    [InlineData("data:image/png,rawtext", null)]
    [InlineData("data:image/png;base64", null)]
    [InlineData(null, "bm90IGFuIGltYWdl")] // "not an image"
    public void Returns_null_when_there_is_no_usable_inline_image(string? url, string? base64)
    {
        Assert.Null(QrImageData.TryDecode(url, base64));
    }

    [Theory]
    [InlineData("https://example.com/qr.png", true)]
    [InlineData("http://example.com/qr.png", true)]
    [InlineData("data:image/png;base64,AAAA", false)]
    [InlineData("file:///c:/qr.png", false)]
    [InlineData("qr.png", false)]
    [InlineData(null, false)]
    public void RemoteUri_is_only_a_real_web_address(string? url, bool isRemote)
    {
        Assert.Equal(isRemote, QrImageData.RemoteUri(url) is not null);
    }
}

using BiSheng.Server.Services.Images;

namespace BiSheng.Server.Tests.Images;

/// <summary>PR5：上传图片魔数校验</summary>
public class ImageContentValidatorTests
{
    /// <summary>合法 PNG 头 + .png 通过</summary>
    [Fact]
    public void TryValidate_PngHeader_WithPngExt_Succeeds()
    {
        var header = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x00
        };

        Assert.True(ImageContentValidator.TryValidate(header, ".png", out var contentType, out var error));
        Assert.Equal("image/png", contentType);
        Assert.Null(error);
    }

    /// <summary>JPEG 头允许 .jpg / .jpeg</summary>
    [Theory]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    public void TryValidate_JpegHeader_AcceptsJpgAliases(string ext)
    {
        var header = new byte[]
        {
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46,
            0x49, 0x46, 0x00, 0x01
        };

        Assert.True(ImageContentValidator.TryValidate(header, ext, out var contentType, out _));
        Assert.Equal("image/jpeg", contentType);
    }

    /// <summary>伪装扩展名：JPEG 内容声称 .png 被拒</summary>
    [Fact]
    public void TryValidate_JpegBytes_WithPngExt_Fails()
    {
        var header = new byte[]
        {
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46,
            0x49, 0x46, 0x00, 0x01
        };

        Assert.False(ImageContentValidator.TryValidate(header, ".png", out _, out var error));
        Assert.Contains("JPEG", error);
    }

    /// <summary>纯文本伪装成图片被拒</summary>
    [Fact]
    public void TryValidate_TextBytes_Fails()
    {
        var header = "not-an-image!"u8.ToArray();

        Assert.False(ImageContentValidator.TryValidate(header, ".png", out _, out var error));
        Assert.Contains("无法识别", error);
    }

    /// <summary>WebP RIFF 头通过</summary>
    [Fact]
    public void TryValidate_WebpHeader_Succeeds()
    {
        var header = new byte[]
        {
            (byte)'R', (byte)'I', (byte)'F', (byte)'F',
            0x00, 0x00, 0x00, 0x00,
            (byte)'W', (byte)'E', (byte)'B', (byte)'P'
        };

        Assert.True(ImageContentValidator.TryValidate(header, ".webp", out var contentType, out _));
        Assert.Equal("image/webp", contentType);
    }
}

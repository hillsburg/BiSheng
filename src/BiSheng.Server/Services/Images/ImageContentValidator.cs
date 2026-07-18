namespace BiSheng.Server.Services.Images;

/// <summary>
/// 上传图片魔数（文件头）校验：扩展名须与真实格式一致，拒绝伪装扩展名
/// </summary>
public static class ImageContentValidator
{
    /// <summary>校验所需最小文件头字节数</summary>
    public const int MinHeaderLength = 12;

    /// <summary>
    /// 根据文件头与声明扩展名校验是否为合法图片；成功时返回规范 Content-Type
    /// </summary>
    /// <param name="header">文件开头至少 <see cref="MinHeaderLength"/> 字节</param>
    /// <param name="extension">含点的小写扩展名，如 <c>.png</c></param>
    /// <param name="contentType">检测到的 MIME 类型</param>
    /// <param name="error">失败原因（面向 API 返回）</param>
    /// <returns>是否通过校验</returns>
    public static bool TryValidate(
        ReadOnlySpan<byte> header,
        string extension,
        out string contentType,
        out string? error)
    {
        contentType = string.Empty;
        error = null;

        if (header.Length < MinHeaderLength)
        {
            error = "文件过短，无法识别图片格式";
            return false;
        }

        var ext = NormalizeExtension(extension);
        if (ext is null)
        {
            error = $"不支持的文件格式: {extension}";
            return false;
        }

        if (!TryDetectFormat(header, out var detectedExt, out contentType))
        {
            error = "无法识别的图片文件头";
            return false;
        }

        // jpeg 允许 .jpg / .jpeg 两种扩展名
        if (detectedExt == ".jpeg")
        {
            if (ext is not (".jpg" or ".jpeg"))
            {
                error = $"文件内容为 JPEG，与扩展名 {ext} 不符";
                return false;
            }

            return true;
        }

        if (ext != detectedExt)
        {
            error = $"文件内容为 {detectedExt.TrimStart('.')}，与扩展名 {ext} 不符";
            return false;
        }

        return true;
    }

    /// <summary>将扩展名规范为小写并校验白名单</summary>
    private static string? NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var ext = extension.StartsWith('.')
            ? extension.ToLowerInvariant()
            : "." + extension.ToLowerInvariant();

        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => ext,
            _ => null
        };
    }

    /// <summary>从文件头识别图片格式</summary>
    private static bool TryDetectFormat(
        ReadOnlySpan<byte> header,
        out string extension,
        out string contentType)
    {
        extension = string.Empty;
        contentType = string.Empty;

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (header.Length >= 8
            && header[0] == 0x89
            && header[1] == 0x50
            && header[2] == 0x4E
            && header[3] == 0x47
            && header[4] == 0x0D
            && header[5] == 0x0A
            && header[6] == 0x1A
            && header[7] == 0x0A)
        {
            extension = ".png";
            contentType = "image/png";
            return true;
        }

        // JPEG: FF D8 FF
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            extension = ".jpeg";
            contentType = "image/jpeg";
            return true;
        }

        // GIF87a / GIF89a
        if (header[0] == (byte)'G'
            && header[1] == (byte)'I'
            && header[2] == (byte)'F'
            && header[3] == (byte)'8'
            && (header[4] == (byte)'7' || header[4] == (byte)'9')
            && header[5] == (byte)'a')
        {
            extension = ".gif";
            contentType = "image/gif";
            return true;
        }

        // BMP: BM
        if (header[0] == (byte)'B' && header[1] == (byte)'M')
        {
            extension = ".bmp";
            contentType = "image/bmp";
            return true;
        }

        // WEBP: RIFF....WEBP
        if (header.Length >= 12
            && header[0] == (byte)'R'
            && header[1] == (byte)'I'
            && header[2] == (byte)'F'
            && header[3] == (byte)'F'
            && header[8] == (byte)'W'
            && header[9] == (byte)'E'
            && header[10] == (byte)'B'
            && header[11] == (byte)'P')
        {
            extension = ".webp";
            contentType = "image/webp";
            return true;
        }

        return false;
    }
}

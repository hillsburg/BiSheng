using OtpNet;
using QRCoder;

namespace BiSheng.Server.Services;

/// <summary>
/// TOTP 两步验证工具类：生成密钥、构建 otpauth URI、验证码校验
/// </summary>
public static class TotpHelper
{
    /// <summary>
    /// 生成随机 TOTP 密钥（Base32 编码，20 字节 = 160 位）
    /// </summary>
    public static string GenerateSecret()
    {
        var key = KeyGeneration.GenerateRandomKey(20);
        return Base32Encoding.ToString(key);
    }

    /// <summary>
    /// 构建 otpauth:// URI，供 Google Authenticator 等 App 扫码导入
    /// </summary>
    public static string GetOtpAuthUri(string secret, string username, string issuer = "BiSheng")
    {
        // otpauth://totp/{issuer}:{account}?secret={secret}&issuer={issuer}&algorithm=SHA1&digits=6&period=30
        return $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(username)}" +
               $"?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";
    }

    /// <summary>
    /// 验证 TOTP 验证码（允许前后各 1 步容差，即 ±30 秒）。
    /// 空密钥视为未绑定，直接失败（不抛 Base32 异常）。
    /// </summary>
    public static bool VerifyCode(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code) || code.Length != 6)
        {
            return false;
        }

        try
        {
            var secretBytes = Base32Encoding.ToBytes(secret);
            var totp = new Totp(secretBytes);
            return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 使用 QRCoder 生成二维码，返回 base64 data URI（可直接作为 img src）
    /// </summary>
    public static string GetQrCodeDataUri(string otpAuthUri, int pixelsPerModule = 5)
    {
        var qrGenerator = new QRCodeGenerator();
        var qrCodeData = qrGenerator.CreateQrCode(otpAuthUri, QRCodeGenerator.ECCLevel.M);
        var qrCode = new PngByteQRCode(qrCodeData);
        var qrBytes = qrCode.GetGraphic(pixelsPerModule);
        var base64 = Convert.ToBase64String(qrBytes);
        return $"data:image/png;base64,{base64}";
    }
}

using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace BiSheng.Server.Auth;

/// <summary>
/// 用户 TotpSecret 列的 Data Protection 加解密（兼容历史明文）。
/// </summary>
public sealed class TotpSecretProtector
{
    /// <summary>密文前缀</summary>
    public const string ProtectedPrefix = "prot:v1:";

    private readonly IDataProtector _protector;
    private readonly ILogger<TotpSecretProtector> _logger;

    /// <summary>构造 TOTP 密钥保护器</summary>
    public TotpSecretProtector(IDataProtectionProvider dataProtection, ILogger<TotpSecretProtector> logger)
    {
        _protector = dataProtection.CreateProtector("BiSheng.User.TotpSecret.v1");
        _logger = logger;
    }

    /// <summary>加密明文 TOTP 密钥以便落库</summary>
    public string Protect(string plainSecret)
    {
        if (string.IsNullOrWhiteSpace(plainSecret))
        {
            throw new ArgumentException("TOTP 密钥不能为空", nameof(plainSecret));
        }

        if (IsProtected(plainSecret))
        {
            return plainSecret;
        }

        var bytes = Encoding.UTF8.GetBytes(plainSecret.Trim());
        return ProtectedPrefix + Convert.ToBase64String(_protector.Protect(bytes));
    }

    /// <summary>解密库中的 TOTP 密钥；明文存量原样返回</summary>
    public string Unprotect(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return string.Empty;
        }

        if (!IsProtected(stored))
        {
            return stored.Trim();
        }

        try
        {
            var payload = Convert.FromBase64String(stored[ProtectedPrefix.Length..]);
            return Encoding.UTF8.GetString(_protector.Unprotect(payload));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TOTP 密钥解密失败");
            return string.Empty;
        }
    }

    /// <summary>是否已为受保护格式</summary>
    public static bool IsProtected(string? stored) =>
        !string.IsNullOrEmpty(stored)
        && stored.StartsWith(ProtectedPrefix, StringComparison.Ordinal);
}

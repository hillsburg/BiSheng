using Microsoft.AspNetCore.Identity;

namespace BiSheng.Server.Auth;

public static class PasswordCredentialService
{
    /// <summary>
    /// 使用 PBKDF2（ASP.NET Identity 内置 PasswordHasher）哈希密码
    /// </summary>
    public static string HashPassword(string password)
    {
        return PasswordHasher.HashPassword(null!, password);
    }

    /// <summary>
    /// 存储哈希是否为当前 Identity PasswordHasher 可解析的 Base64 格式。
    /// 旧库或手工种子数据可能不是此格式，直接 Verify 会抛 FormatException。
    /// </summary>
    public static bool IsIdentityPasswordHash(string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        try
        {
            _ = Convert.FromBase64String(storedHash);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// 验证密码（PBKDF2）。哈希为空、格式非法或校验失败时返回 false，不向外抛异常。
    /// </summary>
    public static bool VerifyPassword(string password, string? storedHash)
    {
        if (string.IsNullOrEmpty(password) || !IsIdentityPasswordHash(storedHash))
        {
            return false;
        }

        try
        {
            var result = PasswordHasher.VerifyHashedPassword(null!, storedHash!, password);
            return result == PasswordVerificationResult.Success
                || result == PasswordVerificationResult.SuccessRehashNeeded;
        }
        catch (FormatException)
        {
            // 双重保险：极端情况下 Identity 仍可能抛格式异常
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static readonly PasswordHasher<object> PasswordHasher = new();
}

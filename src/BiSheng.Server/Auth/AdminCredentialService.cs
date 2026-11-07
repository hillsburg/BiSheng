using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using BiSheng.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Auth;

/// <summary>统一账号失败控制、TOTP 防重放及敏感操作凭据验证。</summary>
public sealed class AdminCredentialService(AppDbContext db, TotpSecretProtector protector)
{
    private static readonly string DummyHash = PasswordCredentialService.HashPassword("invalid-account-password");

    /// <summary>验证密码与账号可用状态；未知账号也执行哈希验证，减少时序差异。</summary>
    public async Task<bool> VerifyPasswordAsync(User? user, string password)
    {
        var valid = PasswordCredentialService.VerifyPassword(password, user?.PasswordHash ?? DummyHash);
        if (user == null || !await IsAvailableAsync(user))
        {
            return false;
        }
        if (!valid)
        {
            await RecordFailureAsync(user.Id);
        }
        return valid;
    }

    /// <summary>敏感操作始终验证密码；近期没有完成验证时，额外校验一次性 TOTP。</summary>
    public async Task<bool> AuthorizeSensitiveOperationAsync(HttpContext context, User user, string password, string code, bool development)
    {
        if (!await VerifyPasswordAsync(user, password))
        {
            return false;
        }
        if (development || AdminSessionService.HasRecentVerification(context.User))
        {
            return true;
        }
        return await VerifyTotpAsync(user, code);
    }

    /// <summary>从数据库检查锁定期限和认证版本，避免信任过期的用户快照。</summary>
    public Task<bool> IsAvailableAsync(User user)
    {
        var now = DateTime.UtcNow;
        return db.Users.AsNoTracking().AnyAsync(u => u.Id == user.Id && u.AuthVersion == user.AuthVersion
            && (u.AuthLockedUntilUtc == null || u.AuthLockedUntilUtc <= now));
    }

    /// <summary>通过数据库条件更新原子消费时间步，防止并发请求重复使用验证码。</summary>
    public async Task<bool> VerifyTotpAsync(User user, string code)
    {
        if (!await IsAvailableAsync(user))
        {
            return false;
        }
        if (!TotpHelper.TryVerifyCode(protector.Unprotect(user.TotpSecret), code, out var step))
        {
            await RecordFailureAsync(user.Id);
            return false;
        }
        var now = DateTime.UtcNow;
        var changed = await db.Users.Where(u => u.Id == user.Id && u.AuthVersion == user.AuthVersion
            && u.TotpSecret == user.TotpSecret && (u.LastTotpTimeStep == null || u.LastTotpTimeStep < step)
            && (u.AuthLockedUntilUtc == null || u.AuthLockedUntilUtc <= now))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastTotpTimeStep, step));
        if (changed == 0)
        {
            await RecordFailureAsync(user.Id);
        }
        return changed == 1;
    }

    /// <summary>完整登录成功后清除账号失败计数及锁定状态。</summary>
    public Task ResetFailuresAsync(Guid id) => db.Users.Where(u => u.Id == id)
        .ExecuteUpdateAsync(s => s.SetProperty(u => u.FailedAuthAttempts, 0)
            .SetProperty(u => u.AuthLockedUntilUtc, (DateTime?)null));

    /// <summary>原子累计账号验证失败次数，第十次失败锁定五分钟。</summary>
    private Task RecordFailureAsync(Guid id)
    {
        var now = DateTime.UtcNow;
        var until = now.AddMinutes(5);
        return db.Users.Where(u => u.Id == id && (u.AuthLockedUntilUtc == null || u.AuthLockedUntilUtc <= now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.AuthLockedUntilUtc, u => u.FailedAuthAttempts >= 9 ? until : null)
                .SetProperty(u => u.FailedAuthAttempts, u => u.FailedAuthAttempts >= 9 ? 0 : u.FailedAuthAttempts + 1));
    }
}

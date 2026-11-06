using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BiSheng.Server.Auth;

/// <summary>编排 WebAuthn 仪式及凭据管理；签名、来源、RP ID 和 UV 校验交给 Fido2 库。</summary>
public sealed class PasskeyService(AppDbContext db, IFido2 fido, IOptions<WebAuthnOptions> settings,
    AdminPendingSessionService pending, AdminCredentialService credentials, AdminSessionService sessions)
{
    private const string LoginFailed = "通行密钥验证失败，请重试或改用密码登录。";
    private const string RegistrationFailed = "设备返回的凭据未通过验证，尚未添加通行密钥。";

    public bool Enabled => settings.Value.Enabled;
    private string RpId => settings.Value.RpId;

    /// <summary>生成匿名登录挑战，不返回账号列表或凭据列表。</summary>
    public string BeginLogin(HttpContext context)
    {
        var optionsJson = fido.GetAssertionOptions(new GetAssertionOptionsParams
        {
            UserVerification = UserVerificationRequirement.Required
        }).ToJson();
        pending.SetPasskeyLoginPending(context, new PasskeyLoginState { OptionsJson = optionsJson });
        return optionsJson;
    }

    /// <summary>消费挑战并验证签名，更新计数器后签发后台 Cookie；并发使用同一凭据时只有一次成功。</summary>
    public async Task FinishLoginAsync(HttpContext context, AuthenticatorAssertionRawResponse response)
    {
        if (!pending.TakePasskeyLoginPending(context, out var state))
        {
            throw new PasskeyRejectedException("登录已过期，请重新点击“使用通行密钥登录”。");
        }
        if (response.RawId is not { Length: > 0 } || response.Response == null)
        {
            throw new PasskeyRejectedException(LoginFailed);
        }
        var key = await db.PasskeyCredentials.Include(k => k.User)
            .SingleOrDefaultAsync(k => k.CredentialId == response.RawId && k.RpId == RpId);
        if (key == null || !await credentials.IsAvailableAsync(key.User))
        {
            throw new PasskeyRejectedException(LoginFailed);
        }

        VerifyAssertionResult result;
        try
        {
            result = await fido.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = response,
                OriginalOptions = AssertionOptions.FromJson(state.OptionsJson),
                StoredPublicKey = key.PublicKey,
                StoredSignatureCounter = key.SignCount,
                IsUserHandleOwnerOfCredentialIdCallback = (args, _) =>
                    Task.FromResult(args.UserHandle.SequenceEqual(key.UserHandle))
            });
        }
        catch (Fido2VerificationException e)
        {
            throw new PasskeyRejectedException(LoginFailed, e);
        }
        // BE 是注册时固定的属性，BS 可随同步/备份变化；签名验证通过后才能信任这些位。
        if (AuthenticatorData.Parse(response.Response.AuthenticatorData).IsBackupEligible != key.IsBackupEligible)
        {
            throw new PasskeyRejectedException(LoginFailed);
        }

        key.SignCount = result.SignCount;
        key.IsBackedUp = result.IsBackedUp;
        key.LastUsedAtUtc = DateTime.UtcNow;
        key.Revision = Guid.NewGuid();
        await SaveOrRejectAsync(LoginFailed);
        await credentials.ResetFailuresAsync(key.UserId);
        pending.ClearLoginPending(context);
        await sessions.SignInAsync(context, key.User);
    }

    /// <summary>仅在重新授权后调用；创建可发现凭据并要求认证器完成用户验证。</summary>
    public async Task<string> BeginRegistrationAsync(HttpContext context, User user, string name, bool useOtherDevice = false)
    {
        var existing = await db.PasskeyCredentials.Where(k => k.UserId == user.Id && k.RpId == RpId)
            .Select(k => k.CredentialId).ToListAsync();
        if (existing.Count >= WebAuthnOptions.MaxCredentialsPerUser)
        {
            throw new PasskeyRejectedException($"最多绑定 {WebAuthnOptions.MaxCredentialsPerUser} 把通行密钥。");
        }
        var optionsJson = fido.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = user.Id.ToByteArray(), Name = user.Username, DisplayName = user.Username },
            ExcludeCredentials = existing.Select(id => new PublicKeyCredentialDescriptor(id)).ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                // 本机入口在 Windows 上使用 Windows Hello；其他设备入口允许手机或安全密钥。
                AuthenticatorAttachment = useOtherDevice ? AuthenticatorAttachment.CrossPlatform : AuthenticatorAttachment.Platform,
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required
            },
            AttestationPreference = AttestationConveyancePreference.None
        }).ToJson();
        pending.SetPasskeyRegisterPending(context, new PasskeyRegisterState
        {
            UserId = user.Id, AuthVersion = user.AuthVersion, Name = name, OptionsJson = optionsJson
        });
        return optionsJson;
    }

    /// <summary>绑定用户与授权版本，验证后仅保存公钥；版本更新撤销其他会话，当前会话换发 Cookie。</summary>
    public async Task FinishRegistrationAsync(HttpContext context, User user, AuthenticatorAttestationRawResponse response)
    {
        if (!pending.TakePasskeyRegisterPending(context, out var state)
            || state.UserId != user.Id || state.AuthVersion != user.AuthVersion
            || !await credentials.IsAvailableAsync(user))
        {
            throw new PasskeyRejectedException("绑定授权已过期，请重新填写密码后添加。");
        }

        RegisteredPublicKeyCredential result;
        try
        {
            result = await fido.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = response,
                OriginalOptions = CredentialCreateOptions.FromJson(state.OptionsJson),
                IsCredentialIdUniqueToUserCallback = async (args, cancellationToken) =>
                    !await db.PasskeyCredentials.AnyAsync(k => k.CredentialId == args.CredentialId, cancellationToken)
            });
        }
        catch (Fido2VerificationException e)
        {
            throw new PasskeyRejectedException(RegistrationFailed, e);
        }

        db.PasskeyCredentials.Add(new PasskeyCredential
        {
            UserId = user.Id, CredentialId = result.Id, PublicKey = result.PublicKey,
            UserHandle = user.Id.ToByteArray(), Name = state.Name, RpId = RpId,
            SignCount = result.SignCount, IsBackupEligible = result.IsBackupEligible, IsBackedUp = result.IsBackedUp
        });
        user.AuthVersion++;
        // 唯一索引兜底并发绑定同一把凭据的竞争。
        await SaveOrRejectAsync("这把通行密钥已绑定，请使用其他设备或密码管理器。");
        await sessions.RefreshAsync(context, user);
    }

    /// <summary>只返回展示字段，不将公钥和凭据 ID 暴露给管理页面。</summary>
    public async Task<IReadOnlyList<PasskeySummary>> ListAsync(Guid userId) =>
        await db.PasskeyCredentials.Where(k => k.UserId == userId).OrderBy(k => k.CreatedAtUtc)
            .Select(k => new PasskeySummary(k.Id, k.Name, k.RpId, k.CreatedAtUtc, k.LastUsedAtUtc))
            .ToListAsync();

    /// <summary>名称只作为文本显示，不影响凭据本身，因此无需重新授权。</summary>
    public async Task<bool> RenameAsync(Guid userId, Guid id, string name) =>
        await db.PasskeyCredentials.Where(k => k.Id == id && k.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.Name, name)) == 1;

    /// <summary>删除凭据并撤销其他后台会话及待验证授权，当前会话换发 Cookie。</summary>
    public async Task<bool> DeleteAsync(HttpContext context, User user, Guid id)
    {
        var key = await db.PasskeyCredentials.SingleOrDefaultAsync(k => k.Id == id && k.UserId == user.Id);
        if (key == null)
        {
            return false;
        }
        db.PasskeyCredentials.Remove(key);
        user.AuthVersion++;
        await SaveOrRejectAsync("通行密钥刚刚被使用，请刷新页面后重试。");
        await sessions.RefreshAsync(context, user);
        return true;
    }

    /// <summary>并发冲突和唯一约束冲突属于预期竞争，转换为可展示的拒绝原因。</summary>
    private async Task SaveOrRejectAsync(string message)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e)
        {
            throw new PasskeyRejectedException(message, e);
        }
    }
}

/// <summary>管理页面展示的通行密钥摘要。</summary>
public sealed record PasskeySummary(Guid Id, string Name, string RpId, DateTime CreatedAtUtc, DateTime? LastUsedAtUtc);

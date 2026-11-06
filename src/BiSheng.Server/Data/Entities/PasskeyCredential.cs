using System.ComponentModel.DataAnnotations;

namespace BiSheng.Server.Data.Entities;

/// <summary>通行密钥的公钥记录；私钥始终留在用户的认证器内。</summary>
public sealed class PasskeyCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    /// <summary>认证器返回的原始凭据 ID，规范上限 1023 字节。</summary>
    [MaxLength(1023)]
    public byte[] CredentialId { get; set; } = [];
    public byte[] PublicKey { get; set; } = [];
    public byte[] UserHandle { get; set; } = [];
    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;
    /// <summary>DNS 域名上限 253 字符。</summary>
    [MaxLength(253)]
    public string RpId { get; set; } = string.Empty;
    public uint SignCount { get; set; }
    public bool IsBackupEligible { get; set; }
    public bool IsBackedUp { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAtUtc { get; set; }
    // 计数器为零的同步凭据也需要并发保护，不能仅依赖 SignCount。
    [ConcurrencyCheck]
    public Guid Revision { get; set; } = Guid.NewGuid();
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BiSheng.Server.Data.Entities;

public class ApiKey
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>64 字符随机 Hex 密钥</summary>
    [Required, MaxLength(128)]
    public string KeyValue { get; set; } = string.Empty;

    /// <summary>设备名称（方便管理员辨识）</summary>
    [MaxLength(128)]
    public string DeviceName { get; set; } = string.Empty;

    [Required]
    public Guid UserId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>最近一次成功通过此 Key 认证的时间（UTC）；未使用过为 null</summary>
    public DateTime? LastUsedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}

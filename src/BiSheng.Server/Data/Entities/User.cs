using System.ComponentModel.DataAnnotations;

namespace BiSheng.Server.Data.Entities;

public class User
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(64)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// TOTP 密钥（落库为 Data Protection 密文；历史明文在登录时自动升级）
    /// </summary>
    [Required]
    public string TotpSecret { get; set; } = string.Empty;

    [ConcurrencyCheck]
    public long AuthVersion { get; set; } = 1;
    public long? LastTotpTimeStep { get; set; }
    public int FailedAuthAttempts { get; set; }
    public DateTime? AuthLockedUntilUtc { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Folder> Folders { get; set; } = new List<Folder>();
    public ICollection<Note> Notes { get; set; } = new List<Note>();
}

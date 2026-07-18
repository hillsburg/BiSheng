namespace BiSheng.Latte.Models;

/// <summary>local.db 完整性检查结果</summary>
public sealed class LocalDbIntegrityCheckResult
{
    /// <summary>检查是否通过</summary>
    public bool Ok { get; init; }

    /// <summary>失败或异常时的说明</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>通过</summary>
    public static LocalDbIntegrityCheckResult Passed() =>
        new() { Ok = true, Message = "ok" };

    /// <summary>失败</summary>
    public static LocalDbIntegrityCheckResult Failed(string message) =>
        new() { Ok = false, Message = message };
}

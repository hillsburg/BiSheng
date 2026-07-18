namespace BiSheng.Latte.Models;

/// <summary>数据安全档位：标准 / 保守（更密 Push、备份与历史采样）</summary>
public enum DataSafetyProfile
{
    /// <summary>标准模式：默认 Push、备份与历史采样间隔</summary>
    Balanced = 0,

    /// <summary>保守模式：更短 Push 周期、更密备份与历史采样</summary>
    Conservative = 1
}

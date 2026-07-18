using System.ComponentModel.DataAnnotations;
using BiSheng.Shared;

namespace BiSheng.Latte.Data.Entities;

/// <summary>
/// 同步冲突记录：当本地和远端同时修改同一实体时产生
///
/// 处理流程：
/// 1. Pull 时检测到冲突 → 创建 SyncConflict 记录
/// 2. UI 显示未解决冲突数 → 用户打开冲突弹窗
/// 3. 用户选择"保留本地"/"保留远端"/"手动合并" → 更新本地数据
/// 4. 标记为已解决（IsResolved = true）
/// </summary>
public class SyncConflict
{
    [Key]
    public int Id { get; set; }

    /// <summary>冲突实体类型：<see cref="EntityTypes.Note"/> 或 <see cref="EntityTypes.Folder"/></summary>
    [Required, StringLength(32)]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>冲突实体的唯一标识</summary>
    public Guid EntityId { get; set; }

    /// <summary>实体名称（笔记标题/文件夹名），便于 UI 展示</summary>
    public string EntityTitle { get; set; } = string.Empty;

    /// <summary>本地展示用正文/名称（冲突对话框对比）</summary>
    public string LocalContent { get; set; } = string.Empty;

    /// <summary>远端展示用正文/名称（冲突对话框对比）</summary>
    public string RemoteContent { get; set; } = string.Empty;

    /// <summary>本地操作：Create / Update / Delete</summary>
    [Required, StringLength(16)]
    public string LocalAction { get; set; } = ChangeActions.Update;

    /// <summary>远端操作：Create / Update / Delete</summary>
    [Required, StringLength(16)]
    public string RemoteAction { get; set; } = ChangeActions.Update;

    /// <summary>本地完整同步 payload（JSON）；Delete 时可为空</summary>
    public string? LocalPayload { get; set; }

    /// <summary>远端完整同步 payload（JSON）；Delete 时可为空</summary>
    public string? RemotePayload { get; set; }

    /// <summary>本地变更时间</summary>
    public DateTime LocalUpdatedAt { get; set; }

    /// <summary>远端变更时间</summary>
    public DateTime RemoteUpdatedAt { get; set; }

    /// <summary>是否已解决</summary>
    public bool IsResolved { get; set; }

    /// <summary>冲突检测时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

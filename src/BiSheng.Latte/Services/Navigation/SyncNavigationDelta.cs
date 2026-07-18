using BiSheng.Shared;
using BiSheng.Shared.Sync;
using System.Text.Json;

namespace BiSheng.Latte.Services.Navigation;

/// <summary>单条同步变更对导航 UI 的影响描述</summary>
public sealed record NavigationChange
{
    /// <summary>实体类型：Note / Folder</summary>
    public required string EntityType { get; init; }

    /// <summary>实体 Id</summary>
    public required Guid EntityId { get; init; }

    /// <summary>变更动作：Create / Update / Delete</summary>
    public required string Action { get; init; }

    /// <summary>Note 所属文件夹 Id（Move 时用于列表增删）</summary>
    public Guid? FolderId { get; init; }

    /// <summary>Folder 父级 Id</summary>
    public Guid? ParentFolderId { get; init; }

    /// <summary>收藏/置顶是否变化（需更新收藏区）</summary>
    public bool FlagsChanged { get; init; }

    /// <summary>Folder 父级是否变化（树结构重挂，需全量 Refresh）</summary>
    public bool ParentFolderChanged { get; init; }
}

/// <summary>同步完成后的导航增量描述</summary>
public sealed class SyncNavigationDelta
{
    /// <summary>无远端导航变更</summary>
    public static SyncNavigationDelta Empty { get; } = new();

    /// <summary>必须全量 Refresh（全量同步、结构不确定等）</summary>
    public static SyncNavigationDelta FullRefresh { get; } = new() { RequiresFullRefresh = true };

    /// <summary>是否 fallback 到 FolderTree/NoteList.Refresh()</summary>
    public bool RequiresFullRefresh { get; init; }

    /// <summary>本批变更列表</summary>
    public IReadOnlyList<NavigationChange> Changes { get; init; } = Array.Empty<NavigationChange>();

    /// <summary>从变更列表构造</summary>
    public static SyncNavigationDelta FromChanges(IReadOnlyList<NavigationChange> changes) =>
        changes.Count == 0 ? Empty : new SyncNavigationDelta { Changes = changes };
}

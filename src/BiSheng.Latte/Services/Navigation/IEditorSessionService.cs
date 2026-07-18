namespace BiSheng.Latte.Services.Navigation;

/// <summary>
/// 编辑器会话层：跟踪已加载笔记版本，按读模型变更决定是否从 DB 合并到 UI。
/// </summary>
public interface IEditorSessionService
{
    /// <summary>用户打开笔记时记录已加载的服务端版本</summary>
    void NotifyNoteOpened(Guid noteId, long loadedVersion);

    /// <summary>编辑器关闭当前笔记</summary>
    void NotifyNoteClosed();

    /// <summary>读模型变更后尝试同步当前打开笔记（编辑中由协调器推迟）</summary>
    void ApplyRemoteChanges(SyncNavigationDelta delta);
}

using BiSheng.Latte.Data.Entities;

namespace BiSheng.Latte.Services.Navigation;

/// <summary>
/// 笔记字段合并策略：DB 读模型 → 内存缓存（列表/编辑器）的唯一规则。
/// 防止同步回声用空 DB 覆盖非空编辑器/列表缓存。
/// </summary>
public static class LocalNoteMerger
{
    /// <summary>
    /// 将 source（通常来自 DB）的字段合并到 target（列表或编辑器持有的 LocalNote）。
    /// </summary>
    public static void MergeFields(LocalNote target, LocalNote source)
    {
        target.Title = source.Title;

        // DB 空但当前非空时不覆盖，避免同步竞态清空正文
        if (!(string.IsNullOrEmpty(source.Content) && !string.IsNullOrEmpty(target.Content)))
        {
            target.Content = source.Content;
        }

        target.FolderId = source.FolderId;
        target.UpdatedAt = source.UpdatedAt;
        target.IsDeleted = source.IsDeleted;
        target.IsFavorite = source.IsFavorite;
        target.IsPinned = source.IsPinned;
        if (source.Version > target.Version)
        {
            target.Version = source.Version;
        }
    }

    /// <summary>
    /// 从 DB 读取笔记快照；不存在或已软删返回 null。
    /// </summary>
    public static LocalNote? ReadNoteSnapshot(Data.LocalDbContext db, Guid noteId)
    {
        var note = db.Notes.Find(noteId);
        if (note == null || note.IsDeleted)
        {
            return null;
        }

        return note;
    }
}

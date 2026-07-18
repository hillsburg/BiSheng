using BiSheng.Latte.Data.Entities;

namespace BiSheng.Latte.Services.Navigation;

/// <summary>Folder 字段合并：DB → 树节点缓存</summary>
public static class LocalFolderMerger
{
    /// <summary>将 source 字段合并到 target</summary>
    public static void MergeFields(LocalFolder target, LocalFolder source)
    {
        target.Name = source.Name;
        target.ParentId = source.ParentId;
        target.UpdatedAt = source.UpdatedAt;
        target.IsDeleted = source.IsDeleted;
        target.IsFavorite = source.IsFavorite;
        target.IsPinned = source.IsPinned;
    }
}

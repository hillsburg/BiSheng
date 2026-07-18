using BiSheng.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Services;

/// <summary>文件夹归属与父子关系校验（REST 与 Sync 共用）</summary>
public static class FolderGraphValidator
{
    /// <summary>文件夹存在、属于用户且未删除</summary>
    public static Task<bool> FolderBelongsToUserAsync(
        AppDbContext db,
        Guid userId,
        Guid folderId,
        CancellationToken cancellationToken = default) =>
        FolderExistsAsync(db, userId, folderId, availableFolderIds: null, cancellationToken);

    /// <summary>parentId 可为 null；否则须属于用户且不会形成环</summary>
    public static async Task<bool> IsValidParentAsync(
        AppDbContext db,
        Guid userId,
        Guid folderId,
        Guid? parentId,
        CancellationToken cancellationToken = default) =>
        await IsValidParentAsync(db, userId, folderId, parentId, availableFolderIds: null, cancellationToken);

    /// <summary>Sync Push 批次内：parent 须已在库中或本批已成功应用</summary>
    public static async Task<bool> IsValidParentAsync(
        AppDbContext db,
        Guid userId,
        Guid folderId,
        Guid? parentId,
        IReadOnlySet<Guid>? availableFolderIds,
        CancellationToken cancellationToken = default)
    {
        if (!parentId.HasValue)
        {
            return true;
        }

        if (parentId.Value == folderId)
        {
            return false;
        }

        if (!await FolderExistsAsync(db, userId, parentId.Value, availableFolderIds, cancellationToken))
        {
            return false;
        }

        var visited = new HashSet<Guid>();
        var current = parentId;
        while (current.HasValue)
        {
            if (current.Value == folderId)
            {
                return false;
            }

            if (!visited.Add(current.Value))
            {
                return false;
            }

            var node = db.Folders.Local.FirstOrDefault(
                f => f.Id == current.Value && f.UserId == userId && !f.IsDeleted)
                ?? await db.Folders.AsNoTracking()
                    .FirstOrDefaultAsync(
                        f => f.Id == current.Value && f.UserId == userId && !f.IsDeleted,
                        cancellationToken);
            if (node == null)
            {
                return false;
            }

            current = node.ParentId;
        }

        return true;
    }

    /// <summary>文件夹在库中、ChangeTracker 或本批已成功应用的集合中</summary>
    public static async Task<bool> FolderExistsAsync(
        AppDbContext db,
        Guid userId,
        Guid folderId,
        IReadOnlySet<Guid>? availableFolderIds,
        CancellationToken cancellationToken = default)
    {
        if (db.Folders.Local.Any(f => f.Id == folderId && f.UserId == userId && !f.IsDeleted))
            return true;

        if (availableFolderIds?.Contains(folderId) == true)
            return true;

        return await db.Folders.AnyAsync(
            f => f.Id == folderId && f.UserId == userId && !f.IsDeleted,
            cancellationToken);
    }
}

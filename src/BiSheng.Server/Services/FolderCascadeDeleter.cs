using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using BiSheng.Shared;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Services;

/// <summary>
/// 文件夹级联软删助手：删除 folder 时递归软删所有子孙 folder 和直属 note，
/// 每个被删实体独立分配版本号 + 写 SyncLog，全部在调用方事务内原子提交。
/// F：消除"删 folder 后 note 变孤儿指向已删 folder"的数据不一致
/// </summary>
public static class FolderCascadeDeleter
{
    /// <summary>级联删除产生的变更项，供调用方做 SignalR 通知</summary>
    public sealed record CascadeEntry(string EntityType, Guid EntityId, long Version, DateTime UpdatedAt);

    /// <summary>
    /// 级联软删 rootFolder 之外的所有子孙 folder 及其直属 note。
    /// root folder 自身由调用方处理（分配版本、设 IsDeleted、写 SyncLog），本方法只处理子孙。
    /// </summary>
    /// <param name="db">数据库上下文（须在事务内）</param>
    /// <param name="versionService">版本分配服务</param>
    /// <param name="userId">用户 ID</param>
    /// <param name="rootFolderId">被删的根 folder ID（自身不在结果中）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>所有被级联软删的实体列表（不含 root）</returns>
    public static async Task<List<CascadeEntry>> CascadeDeleteDescendantsAsync(
        AppDbContext db,
        UserSyncVersionService versionService,
        Guid userId,
        Guid rootFolderId,
        CancellationToken ct = default)
    {
        var result = new List<CascadeEntry>();
        var now = DateTime.UtcNow;

        // BFS 收集所有子孙 folder（不含 root）
        var allDescendantFolderIds = new List<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(rootFolderId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var childIds = await db.Folders
                .Where(f => f.ParentId == current && f.UserId == userId && !f.IsDeleted)
                .Select(f => f.Id)
                .ToListAsync(ct);
            foreach (var childId in childIds)
            {
                allDescendantFolderIds.Add(childId);
                queue.Enqueue(childId);
            }
        }

        // 软删所有子孙 folder
        foreach (var folderId in allDescendantFolderIds)
        {
            var folder = await db.Folders.FirstOrDefaultAsync(
                f => f.Id == folderId && f.UserId == userId, ct);
            if (folder == null || folder.IsDeleted)
            {
                continue;
            }

            var version = await versionService.ReserveNextVersionAsync(db, userId, ct);
            folder.IsDeleted = true;
            folder.Version = version;
            folder.UpdatedAt = now;

            db.SyncLogs.Add(new SyncLog
            {
                EntityType = EntityTypes.Folder,
                EntityId = folderId,
                Action = ChangeActions.Delete,
                Version = version,
                UserId = userId
            });

            result.Add(new CascadeEntry(EntityTypes.Folder, folderId, version, now));
        }

        // 软删 root 及所有子孙 folder 下的直属 note
        var allDeletedFolderIds = new List<Guid> { rootFolderId }.Concat(allDescendantFolderIds).ToList();
        var notesToDelete = await db.Notes
            .Where(n => n.UserId == userId && !n.IsDeleted && allDeletedFolderIds.Contains(n.FolderId))
            .ToListAsync(ct);

        foreach (var note in notesToDelete)
        {
            var version = await versionService.ReserveNextVersionAsync(db, userId, ct);
            note.IsDeleted = true;
            note.Version = version;
            note.UpdatedAt = now;

            db.SyncLogs.Add(new SyncLog
            {
                EntityType = EntityTypes.Note,
                EntityId = note.Id,
                Action = ChangeActions.Delete,
                Version = version,
                UserId = userId
            });

            result.Add(new CascadeEntry(EntityTypes.Note, note.Id, version, now));
        }

        return result;
    }
}

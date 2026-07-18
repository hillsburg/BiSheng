using System.Text.Json;
using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Shared;
using BiSheng.Shared.Sync;

namespace BiSheng.Latte.Services.Sync;

/// <summary>
/// 待推送队列排序、依赖补全与游标维护
/// </summary>
internal static class SyncPendingOrdering
{
    /// <summary>补全 pending 引用的文件夹及其父链（离线期间可能未全部入队）</summary>
    internal static void EnsurePendingIncludesDependencies(LocalDbContext db)
    {
        var pendingIds = db.PendingChanges.Select(p => p.EntityId).ToHashSet();
        var requiredFolderIds = new Queue<Guid>();
        var added = 0;

        foreach (var pending in db.PendingChanges.ToList())
        {
            if (pending.EntityType == EntityTypes.Note)
            {
                using var doc = JsonDocument.Parse(pending.Payload ?? "{}");
                var folderId = SyncPayloadReader.ReadGuid(doc.RootElement, "folderId");
                if (folderId != Guid.Empty)
                    requiredFolderIds.Enqueue(folderId);
            }
            else if (pending.EntityType == EntityTypes.Folder)
            {
                using var doc = JsonDocument.Parse(pending.Payload ?? "{}");
                var parentId = SyncPayloadReader.ReadNullableGuid(doc.RootElement, "parentId");
                if (parentId.HasValue)
                    requiredFolderIds.Enqueue(parentId.Value);
            }
        }

        while (requiredFolderIds.Count > 0)
        {
            var folderId = requiredFolderIds.Dequeue();
            if (pendingIds.Contains(folderId))
                continue;

            var folder = db.Folders.FirstOrDefault(f => f.Id == folderId && !f.IsDeleted);
            if (folder == null)
                continue;

            db.PendingChanges.Add(new LocalPendingChange
            {
                EntityType = EntityTypes.Folder,
                EntityId = folder.Id,
                Action = ChangeActions.Create,
                Payload = SyncPayloadJson.Serialize(
                    SyncPayloadBuilder.Folder(folder.Name, folder.ParentId, folder.IsFavorite, folder.IsPinned)),
                UpdatedAt = folder.UpdatedAt
            });
            pendingIds.Add(folder.Id);
            added++;

            if (folder.ParentId.HasValue)
                requiredFolderIds.Enqueue(folder.ParentId.Value);
        }

        if (added > 0)
        {
            db.SaveChangesWithLock();
            LogHelper.Info("补全待推送队列：新增 {0} 个缺失文件夹", added);
        }
    }

    /// <summary>
    /// 排序 pending 用于推送。M：返回环中 folder 的 EntityId 供调用方跳过/告警
    /// </summary>
    internal static (List<LocalPendingChange> Sorted, HashSet<Guid> CyclicIds) OrderPendingForPush(
        List<LocalPendingChange> pending)
    {
        var folderChanges = pending.Where(p => p.EntityType == EntityTypes.Folder).ToList();
        var noteChanges = pending.Where(p => p.EntityType == EntityTypes.Note).ToList();
        var otherChanges = pending
            .Where(p => p.EntityType != EntityTypes.Folder && p.EntityType != EntityTypes.Note)
            .ToList();

        var (sortedFolders, cyclicIds) = TopologicalSortFolderPending(folderChanges);
        return (sortedFolders.Concat(noteChanges).Concat(otherChanges).ToList(), cyclicIds);
    }

    /// <summary>
    /// 对文件夹 pending 做拓扑排序，保证父级先于子级推送。
    /// M：检测 parent 环，环中 folder 从结果排除并记入 cyclicIds 供调用方处理
    /// </summary>
    internal static (List<LocalPendingChange> Sorted, HashSet<Guid> CyclicIds) TopologicalSortFolderPending(
        List<LocalPendingChange> folderChanges)
    {
        var cyclicIds = new HashSet<Guid>();
        if (folderChanges.Count <= 1)
            return (folderChanges, cyclicIds);

        var byId = folderChanges.ToDictionary(c => c.EntityId);
        var parentOf = new Dictionary<Guid, Guid?>();

        foreach (var change in folderChanges)
        {
            if (change.Action == ChangeActions.Delete)
            {
                parentOf[change.EntityId] = null;
                continue;
            }

            using var payload = JsonDocument.Parse(change.Payload ?? "{}");
            parentOf[change.EntityId] = SyncPayloadReader.ReadNullableGuid(payload.RootElement, "parentId");
        }

        var sorted = new List<LocalPendingChange>();
        var inStack = new HashSet<Guid>();
        var stack = new List<Guid>();
        var visited = new HashSet<Guid>();

        void Visit(Guid id)
        {
            if (visited.Contains(id) || !byId.ContainsKey(id))
                return;

            if (inStack.Contains(id))
            {
                // M：命中递归栈中的节点 → 环；从首次出现位置到栈顶都是环成员
                var start = stack.IndexOf(id);
                for (int i = start; i < stack.Count; i++)
                    cyclicIds.Add(stack[i]);
                return;
            }

            inStack.Add(id);
            stack.Add(id);

            var parentId = parentOf.GetValueOrDefault(id);
            if (parentId.HasValue && byId.ContainsKey(parentId.Value))
                Visit(parentId.Value);

            stack.RemoveAt(stack.Count - 1);
            inStack.Remove(id);
            visited.Add(id);

            if (!cyclicIds.Contains(id))
                sorted.Add(byId[id]);
        }

        foreach (var change in folderChanges)
            Visit(change.EntityId);

        return (sorted, cyclicIds);
    }

    /// <summary>更新 SyncState 游标（始终基于当前 DbContext 内 Id=1 的行）</summary>
    internal static void UpsertLastSyncVersion(LocalDbContext db, long version)
    {
        if (version <= 0) return;

        var state = db.SyncState.Find(1);
        if (state == null)
        {
            db.SyncState.Add(new LocalSyncState { Id = 1, LastSyncVersion = version });
            return;
        }

        if (version > state.LastSyncVersion)
            state.LastSyncVersion = version;
    }

    /// <summary>从待推送队列移除记录（优先 Id；Id 未回填时按 EntityType+EntityId 匹配）</summary>
    internal static void RemovePendingChange(LocalDbContext db, LocalPendingChange pending)
    {
        if (pending.Id != 0)
        {
            var tracked = db.PendingChanges.Find(pending.Id);
            if (tracked != null)
            {
                db.PendingChanges.Remove(tracked);
                return;
            }
        }

        var match = db.PendingChanges.Local.FirstOrDefault(p =>
                p.EntityType == pending.EntityType && p.EntityId == pending.EntityId)
            ?? db.PendingChanges.FirstOrDefault(p =>
                p.EntityType == pending.EntityType && p.EntityId == pending.EntityId);

        if (match != null)
        {
            db.PendingChanges.Remove(match);
        }
    }

    /// <summary>批量从待推送队列移除记录</summary>
    internal static void RemovePendingChanges(LocalDbContext db, IEnumerable<LocalPendingChange> pendingItems)
    {
        foreach (var item in pendingItems.ToList())
            RemovePendingChange(db, item);
    }
}

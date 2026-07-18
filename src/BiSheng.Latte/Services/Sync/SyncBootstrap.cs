using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Shared;
using BiSheng.Shared.Sync;

namespace BiSheng.Latte.Services.Sync;

/// <summary>
/// 启动引导：空副本重置游标与首次连接初始推送
/// </summary>
internal sealed class SyncBootstrap
{
    private readonly ApiClient _apiClient;
    private readonly Func<LocalDbContext> _dbFactory;

    /// <summary>创建启动引导器</summary>
    internal SyncBootstrap(ApiClient apiClient, Func<LocalDbContext> dbFactory)
    {
        _apiClient = apiClient;
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// 本地无业务数据时重置同步游标，避免删除数据库后残留 SyncState 导致拉取不完整。
    /// 若仍有 pending（尤其 Delete）或仅有软删实体，不得清空队列/归零游标，否则 Pull 会复活云端数据。
    /// </summary>
    internal Task EnsureFreshLocalReplicaAsync()
    {
        using var db = _dbFactory();
        var hasActiveData = db.Folders.Any(f => !f.IsDeleted) || db.Notes.Any(n => !n.IsDeleted);
        if (hasActiveData)
        {
            return Task.CompletedTask;
        }

        // 软删残骸或未推送变更：不是「空副本」
        var hasPending = db.PendingChanges.Any();
        var hasSoftDeleted = db.Folders.Any(f => f.IsDeleted) || db.Notes.Any(n => n.IsDeleted);
        if (hasPending || hasSoftDeleted)
        {
            LogHelper.Info(
                "跳过空副本重置：pending={0}, softDeleted={1}",
                hasPending, hasSoftDeleted);
            return Task.CompletedTask;
        }

        var state = db.SyncState.FirstOrDefault();
        if (state != null && state.LastSyncVersion != 0)
        {
            state.LastSyncVersion = 0;
            db.SyncState.Update(state);
            db.SaveChangesWithLock();
            LogHelper.Info("检测到空本地副本，已重置同步游标");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 首次连接时，将本地已有数据全部加入待推送队列
    /// 触发条件：服务端版本为 0（服务端无数据）且本地有未推送的数据
    /// </summary>
    internal async Task EnsureInitialSyncAsync()
    {
        using var db = _dbFactory();
        SyncPendingOrdering.EnsurePendingIncludesDependencies(db);

        // 检查服务端是否已有数据
        long serverVersion;
        try
        {
            serverVersion = await _apiClient.GetAsync<long>("/api/sync/version");
            if (serverVersion > 0) return; // 服务端已有数据，跳过初始推送
        }
        catch
        {
            return; // 无法连接服务端，跳过
        }

        // 服务端版本为 0，将本地所有未删除数据加入待推送队列（去重：已存在的跳过）
        var existingPendingIds = db.PendingChanges.Select(p => p.EntityId).ToHashSet();

        var folders = db.Folders.Where(f => !f.IsDeleted).ToList();
        var notes = db.Notes.Where(n => !n.IsDeleted).ToList();

        var added = 0;

        foreach (var folder in folders)
        {
            if (existingPendingIds.Contains(folder.Id)) continue;
            db.PendingChanges.Add(new LocalPendingChange
            {
                EntityType = EntityTypes.Folder,
                EntityId = folder.Id,
                Action = ChangeActions.Create,
                Payload = SyncPayloadJson.Serialize(SyncPayloadBuilder.Folder(folder.Name, folder.ParentId, folder.IsFavorite, folder.IsPinned)),
                UpdatedAt = folder.UpdatedAt
            });
            added++;
        }

        foreach (var note in notes)
        {
            if (existingPendingIds.Contains(note.Id)) continue;
            db.PendingChanges.Add(new LocalPendingChange
            {
                EntityType = EntityTypes.Note,
                EntityId = note.Id,
                Action = ChangeActions.Create,
                Payload = SyncPayloadJson.Serialize(SyncPayloadBuilder.Note(note.Title, note.Content, note.FolderId, note.IsFavorite, note.IsPinned)),
                UpdatedAt = note.UpdatedAt
            });
            added++;
        }

        if (added > 0)
        {
            db.SaveChangesWithLock();
            LogHelper.Info("初始同步：将本地 {0} 个实体加入推送队列", added);
        }
    }
}

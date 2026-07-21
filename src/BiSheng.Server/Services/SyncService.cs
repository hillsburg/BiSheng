using System.Text.Json;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using BiSheng.Server.Services.Mutations;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Services;

/// <summary>
/// 同步业务逻辑：Pull / Push / 版本查询
/// </summary>
public class SyncService : ISyncService
{
    /// <summary>
    /// Push 冲突扫描 SyncLog 行数上限；超过则要求客户端全量重建，避免无界 ToList 内存打爆。
    /// </summary>
    public const int MaxMissedSyncLogsForPushConflict = 2000;

    private readonly AppDbContext _db;
    private readonly ClientSyncStateService _clientSyncState;
    private readonly UserSyncVersionService _versionService;
    private readonly IEntityChangeWriter _entityChangeWriter;
    private readonly ISyncChangeNotifier _syncChangeNotifier;
    private readonly ILogger<SyncService> _logger;

    /// <summary>构造同步服务</summary>
    public SyncService(
        AppDbContext db,
        ClientSyncStateService clientSyncState,
        UserSyncVersionService versionService,
        IEntityChangeWriter entityChangeWriter,
        ISyncChangeNotifier syncChangeNotifier,
        ILogger<SyncService> logger)
    {
        _db = db;
        _clientSyncState = clientSyncState;
        _versionService = versionService;
        _entityChangeWriter = entityChangeWriter;
        _syncChangeNotifier = syncChangeNotifier;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<long> GetServerVersionAsync(
        Guid userId,
        Guid apiKeyId,
        CancellationToken ct = default)
    {
        var serverVersion = await _versionService.GetCurrentVersionAsync(_db, userId, ct);
        // 仅刷新 LastSeenAt，不推进 LastSyncVersion——version 探测未真正 Pull，
        // 误推进会污染 SyncLog 裁剪线（min(活跃设备 LastSyncVersion)）导致丢改
        await _clientSyncState.TouchAsync(_db, userId, apiKeyId, ct);
        await _db.SaveChangesAsync(ct);
        return serverVersion;
    }

    /// <summary>分页 Pull 默认每批 SyncLog 条数</summary>
    public const int DefaultPullLimit = 200;

    /// <summary>分页 Pull 时单批最多读取的 SyncLog 条数上限</summary>
    public const int MaxPullLimit = 500;

    /// <inheritdoc />
    public async Task<SyncPullResponse> PullAsync(
        Guid userId,
        Guid apiKeyId,
        long since,
        int limit = 0,
        long snapshotOffset = 0,
        CancellationToken ct = default)
    {
        var tipVersion = await _versionService.GetCurrentVersionAsync(_db, userId, ct);

        // since≤0：实体快照重建（压缩后唯一可靠的全量路径）
        if (since <= 0)
        {
            return await PullEntitySnapshotAsync(userId, apiKeyId, tipVersion, limit, snapshotOffset, ct);
        }

        if (await _clientSyncState.RequiresFullSyncAsync(_db, userId, since, ct))
        {
            await _clientSyncState.TouchAsync(_db, userId, apiKeyId, ct);
            await _db.SaveChangesAsync(ct);
            return new SyncPullResponse
            {
                Changes = new List<ChangeDto>(),
                ServerVersion = tipVersion,
                NextSince = since,
                HasMore = false,
                RequiresFullSync = true
            };
        }

        // 始终分页：未指定或非法 limit 时使用默认页大小
        var pageSize = limit > 0
            ? Math.Clamp(limit, 1, MaxPullLimit)
            : DefaultPullLimit;

        var batch = await _db.SyncLogs
            .Where(s => s.UserId == userId && s.Version > since)
            .OrderBy(s => s.Version)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = batch.Count > pageSize;
        var logs = hasMore ? batch.Take(pageSize).ToList() : batch;

        if (logs.Count == 0)
        {
            await _clientSyncState.UpsertAsync(_db, userId, apiKeyId, tipVersion, ct);
            await _db.SaveChangesAsync(ct);
            return new SyncPullResponse
            {
                Changes = new List<ChangeDto>(),
                ServerVersion = tipVersion,
                NextSince = tipVersion,
                HasMore = false
            };
        }

        // 本批消费到的最高 SyncLog 版本（客户端下次 since）
        var nextSince = logs.Max(s => s.Version);

        // 终态折叠：同一 EntityId 只保留本批内最新一条 SyncLog
        var entityFinalStates = logs
            .GroupBy(s => s.EntityId)
            .Select(g => new
            {
                EntityId = g.Key,
                FinalLog = g.OrderByDescending(s => s.Version).First()
            })
            .ToList();

        var changes = new List<ChangeDto>();
        foreach (var ef in entityFinalStates)
        {
            var dto = await SyncChangeDtoBuilder.BuildFromEntityAsync(
                _db, userId, ef.FinalLog.EntityType, ef.EntityId, ef.FinalLog, nextSince, ct);
            if (dto != null)
            {
                changes.Add(dto);
            }
        }

        // 有后续批次时只推进到 nextSince；最后一批推进到 tip
        var cursorVersion = hasMore ? nextSince : tipVersion;
        await _clientSyncState.UpsertAsync(_db, userId, apiKeyId, cursorVersion, ct);
        await _db.SaveChangesAsync(ct);

        return new SyncPullResponse
        {
            Changes = changes,
            ServerVersion = tipVersion,
            NextSince = nextSince,
            HasMore = hasMore
        };
    }

    /// <summary>
    /// 导出当前用户 Folder + Note 表态为分页 ChangeDto（先文件夹后笔记，稳定按 Id 排序）。
    /// 中间页不推进 LastSyncVersion；末页推进到 tip。
    /// 使用 Count + OrderBy Id + Skip/Take 批量取实体，避免全量 Id 列表与逐条查询。
    /// </summary>
    private async Task<SyncPullResponse> PullEntitySnapshotAsync(
        Guid userId,
        Guid apiKeyId,
        long tipVersion,
        int limit,
        long snapshotOffset,
        CancellationToken ct)
    {
        var pageSize = limit > 0
            ? Math.Clamp(limit, 1, MaxPullLimit)
            : DefaultPullLimit;

        var offset = snapshotOffset < 0 ? 0 : snapshotOffset;

        var folderCount = await _db.Folders.AsNoTracking()
            .CountAsync(f => f.UserId == userId, ct);
        var noteCount = await _db.Notes.AsNoTracking()
            .CountAsync(n => n.UserId == userId, ct);
        var total = (long)folderCount + noteCount;

        if (offset >= total)
        {
            await _clientSyncState.UpsertAsync(_db, userId, apiKeyId, tipVersion, ct);
            await _db.SaveChangesAsync(ct);
            return new SyncPullResponse
            {
                Changes = new List<ChangeDto>(),
                ServerVersion = tipVersion,
                NextSince = tipVersion,
                HasMore = false,
                IsEntitySnapshot = true
            };
        }

        var changes = new List<ChangeDto>(pageSize);
        var index = offset;
        var remaining = pageSize;

        if (index < folderCount && remaining > 0)
        {
            var folderSkip = checked((int)index);
            var folderTake = (int)Math.Min(remaining, folderCount - index);
            var folders = await _db.Folders.AsNoTracking()
                .Where(f => f.UserId == userId)
                .OrderBy(f => f.Id)
                .Skip(folderSkip)
                .Take(folderTake)
                .ToListAsync(ct);

            foreach (var folder in folders)
            {
                var dto = SyncChangeDtoBuilder.BuildFromLiveFolder(folder);
                if (dto != null)
                {
                    changes.Add(dto);
                }
            }

            index += folderTake;
            remaining -= folderTake;
        }

        if (remaining > 0 && index < total)
        {
            var noteSkip = checked((int)(index - folderCount));
            var noteTake = (int)Math.Min(remaining, total - index);
            var notes = await _db.Notes.AsNoTracking()
                .Where(n => n.UserId == userId)
                .OrderBy(n => n.Id)
                .Skip(noteSkip)
                .Take(noteTake)
                .ToListAsync(ct);

            foreach (var note in notes)
            {
                var dto = SyncChangeDtoBuilder.BuildFromLiveNote(note);
                if (dto != null)
                {
                    changes.Add(dto);
                }
            }

            index += noteTake;
        }

        var nextOffset = index;
        var hasMore = nextOffset < total;

        if (hasMore)
        {
            await _clientSyncState.TouchAsync(_db, userId, apiKeyId, ct);
        }
        else
        {
            await _clientSyncState.UpsertAsync(_db, userId, apiKeyId, tipVersion, ct);
        }

        await _db.SaveChangesAsync(ct);

        return new SyncPullResponse
        {
            Changes = changes,
            ServerVersion = tipVersion,
            // 有后续页：NextSince 承载下一 snapshotOffset；末页对齐 tip
            NextSince = hasMore ? nextOffset : tipVersion,
            HasMore = hasMore,
            IsEntitySnapshot = true
        };
    }

    /// <inheritdoc />
    public async Task<SyncPushResponse> PushAsync(
        Guid userId,
        Guid apiKeyId,
        SyncPushRequest request,
        CancellationToken ct = default)
    {
        var currentServerVersion = await _versionService.GetCurrentVersionAsync(_db, userId, ct);

        if (await _clientSyncState.RequiresFullSyncAsync(_db, userId, request.ClientVersion, ct))
        {
            await _clientSyncState.TouchAsync(_db, userId, apiKeyId, ct);
            await _db.SaveChangesAsync(ct);
            return new SyncPushResponse
            {
                Success = false,
                ServerVersion = currentServerVersion,
                RequiresFullSync = true,
                Errors = new List<string> { "本地同步版本过旧，请执行全量同步" }
            };
        }

        var errors = new List<string>();
        var conflictingChanges = new List<ChangeDto>();
        var overwrittenChanges = new List<ChangeDto>();
        var failedEntityIds = new List<Guid>();
        var (orderedChanges, cyclicFolderIds) = OrderChangesForPush(request.Changes);

        // M：成环的 folder 直接记入失败，不参与应用
        foreach (var cyclicId in cyclicFolderIds)
        {
            failedEntityIds.Add(cyclicId);
            errors.Add($"文件夹 {cyclicId} 父子关系成环，已跳过");
        }

        var appliedChanges = new List<AppliedMutation>();

        using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // 仅统计已成功应用或已在库中的文件夹，避免 FK 引用尚未创建的父级
            var batchContext = new MutationBatchContext
            {
                AvailableFolderIds = new HashSet<Guid>(
                    await _db.Folders
                        .Where(f => f.UserId == userId && !f.IsDeleted)
                        .Select(f => f.Id)
                        .ToListAsync(ct))
            };

            foreach (var change in orderedChanges)
            {
                try
                {
                    var result = await _entityChangeWriter.TryApplyAsync(
                        _db,
                        userId,
                        change.ToEntityMutation(),
                        batchContext,
                        new MutationWriteOptions(),
                        ct);

                    if (result is MutationApplied { Applied: var applied })
                    {
                        appliedChanges.Add(applied);
                        if (change.EntityType == EntityTypes.Folder && change.Action != ChangeActions.Delete)
                        {
                            batchContext.AvailableFolderIds.Add(change.EntityId);
                        }
                    }
                    else
                    {
                        errors.Add($"跳过无效变更 {change.EntityType}:{change.EntityId}（实体不存在或校验失败）");
                        failedEntityIds.Add(change.EntityId);
                        _logger.LogWarning(
                            "跳过无效变更 {EntityType}:{EntityId}, 用户: {UserId}",
                            change.EntityType, change.EntityId, userId);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"应用变更 {change.EntityType}:{change.EntityId} 失败: {ex.Message}");
                    failedEntityIds.Add(change.EntityId);
                    _logger.LogWarning(ex,
                        "应用变更失败 {EntityType}:{EntityId}, 用户: {UserId}",
                        change.EntityType, change.EntityId, userId);
                }
            }

            // Push 不能把设备游标抬到 tip/本批版本：ClientVersion 之后的他人 SyncLog 尚未 Pull，
            // 误推进会让裁剪线过分乐观。仅将游标对齐到客户端声称已追上的水位并刷新 LastSeenAt。
            await _clientSyncState.UpsertAsync(_db, userId, apiKeyId, request.ClientVersion, ct);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex,
                "同步 Push 事务失败, 用户: {UserId}, 变更数: {Count}",
                userId, request.Changes.Count);

            // 回滚后读取真实水位，避免 0 与「服务端无数据」混淆；
            // 客户端可据 TransactionRolledBack 保留 pending 重试
            var rolledBackVersion = await _versionService.GetCurrentVersionAsync(_db, userId, ct);
            return new SyncPushResponse
            {
                Success = false,
                ServerVersion = rolledBackVersion,
                Errors = new List<string> { "事务提交失败" },
                ConflictingChanges = new List<ChangeDto>(),
                OverwrittenChanges = new List<ChangeDto>(),
                FailedEntityIds = failedEntityIds,
                TransactionRolledBack = true
            };
        }

        // 事务提交后通知同用户其他在线客户端
        await _syncChangeNotifier.NotifyBatchAsync(userId, appliedChanges, ct);

        var serverVersion = await _versionService.GetCurrentVersionAsync(_db, userId, ct);

        // 客户端版本落后时，返回需补拉的冲突变更（排除本批已应用的实体）
        var requiresFullSyncFromGap = false;
        if (request.ClientVersion < serverVersion)
        {
            var missedCount = await _db.SyncLogs
                .CountAsync(s => s.UserId == userId && s.Version > request.ClientVersion, ct);

            if (missedCount > MaxMissedSyncLogsForPushConflict)
            {
                // 版本差过大：避免一次性加载全部 SyncLog
                requiresFullSyncFromGap = true;
                _logger.LogWarning(
                    "Push 冲突扫描 SyncLog 过多 ({Count} > {Max})，要求全量同步。用户: {UserId}, ClientVersion: {ClientVersion}",
                    missedCount, MaxMissedSyncLogsForPushConflict, userId, request.ClientVersion);
            }
            else
            {
                var appliedEntityIds = appliedChanges
                    .Select(x => x.Mutation.EntityId)
                    .ToHashSet();

                var missedLogs = await _db.SyncLogs
                    .Where(s => s.UserId == userId && s.Version > request.ClientVersion)
                    .ToListAsync(ct);

                var entityFinalStates = missedLogs
                    .GroupBy(s => s.EntityId)
                    .Select(g => new
                    {
                        EntityId = g.Key,
                        FinalLog = g.OrderByDescending(s => s.Version).First()
                    })
                    .Where(x => !appliedEntityIds.Contains(x.EntityId))
                    .ToList();

                foreach (var ef in entityFinalStates)
                {
                    var dto = await SyncChangeDtoBuilder.BuildFromEntityAsync(
                        _db, userId, ef.FinalLog.EntityType, ef.EntityId, ef.FinalLog, serverVersion, ct);
                    if (dto != null)
                    {
                        conflictingChanges.Add(dto);
                    }
                }

                // E：冲突盲区——本批覆盖的远端 pre-state。
                // 对 appliedEntityIds 中的实体，取本批赋版前的最高 SyncLog（即被覆盖的远端终态），
                // 直接用 SyncLog.Payload 构建 ChangeDto（不能用当前实体态，那已是客户端版本）
                var batchVersionByEntity = appliedChanges
                    .GroupBy(x => x.Mutation.EntityId)
                    .ToDictionary(g => g.Key, g => g.Max(x => x.Version));

                var overwrittenLogs = missedLogs
                    .Where(s => batchVersionByEntity.ContainsKey(s.EntityId)
                                && s.Version < batchVersionByEntity[s.EntityId])
                    .GroupBy(s => s.EntityId)
                    .Select(g => g.OrderByDescending(s => s.Version).First())
                    .ToList();

                foreach (var log in overwrittenLogs)
                {
                    overwrittenChanges.Add(BuildChangeDtoFromSyncLog(log));
                }
            }
        }

        if (errors.Count > 0)
        {
            _logger.LogWarning(
                "同步 Push 部分失败, 用户: {UserId}, 失败: {Failed}, 成功: {Applied}",
                userId, failedEntityIds.Count, appliedChanges.Count);
        }

        return new SyncPushResponse
        {
            Success = errors.Count == 0,
            ServerVersion = serverVersion,
            Errors = errors,
            ConflictingChanges = conflictingChanges,
            OverwrittenChanges = overwrittenChanges,
            FailedEntityIds = failedEntityIds,
            RequiresFullSync = requiresFullSyncFromGap
        };
    }

    /// <summary>
    /// E：从 SyncLog 直接构建 ChangeDto（用于 OverwrittenChanges 的远端 pre-state）。
    /// 不能用 BuildChangeDtoFromEntity——此时实体已是客户端版本，需用 SyncLog.Payload 还原被覆盖的远端态
    /// </summary>
    private static ChangeDto BuildChangeDtoFromSyncLog(SyncLog log)
    {
        return new ChangeDto
        {
            EntityType = log.EntityType,
            EntityId = log.EntityId,
            Action = log.Action,
            Version = log.Version,
            Timestamp = log.Timestamp,
            Payload = log.Payload
        };
    }

    /// <summary>
    /// 文件夹先于笔记；文件夹按 parent 依赖拓扑排序。
    /// M：检测 parent 环，环中 folder 从结果排除并返回供调用方记入失败
    /// </summary>
    private static (List<ClientChangeDto> Sorted, HashSet<Guid> CyclicIds) OrderChangesForPush(
        IReadOnlyList<ClientChangeDto> changes)
    {
        var folderChanges = changes.Where(c => c.EntityType == EntityTypes.Folder).ToList();
        var noteChanges = changes.Where(c => c.EntityType == EntityTypes.Note).ToList();
        var otherChanges = changes
            .Where(c => c.EntityType != EntityTypes.Folder && c.EntityType != EntityTypes.Note)
            .ToList();

        var (sortedFolders, cyclicIds) = TopologicalSortFolders(folderChanges);
        return (sortedFolders.Concat(noteChanges).Concat(otherChanges).ToList(), cyclicIds);
    }

    /// <summary>对文件夹变更做拓扑排序，保证父级先于子级创建；成环的 folder 被排除</summary>
    private static (List<ClientChangeDto>, HashSet<Guid>) TopologicalSortFolders(
        List<ClientChangeDto> folderChanges)
    {
        var cyclicIds = new HashSet<Guid>();
        if (folderChanges.Count <= 1)
        {
            return (folderChanges, cyclicIds);
        }

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

        var sorted = new List<ClientChangeDto>();
        var inStack = new HashSet<Guid>();
        var stack = new List<Guid>();
        var visited = new HashSet<Guid>();

        void Visit(Guid id)
        {
            if (visited.Contains(id) || !byId.ContainsKey(id))
            {
                return;
            }

            if (inStack.Contains(id))
            {
                // M：命中递归栈中的节点 → 环；从首次出现位置到栈顶都是环成员
                var start = stack.IndexOf(id);
                for (int i = start; i < stack.Count; i++)
                {
                    cyclicIds.Add(stack[i]);
                }
                return;
            }

            inStack.Add(id);
            stack.Add(id);

            var parentId = parentOf.GetValueOrDefault(id);
            if (parentId.HasValue && byId.ContainsKey(parentId.Value))
            {
                Visit(parentId.Value);
            }

            stack.RemoveAt(stack.Count - 1);
            inStack.Remove(id);
            visited.Add(id);

            if (!cyclicIds.Contains(id))
            {
                sorted.Add(byId[id]);
            }
        }

        foreach (var change in folderChanges)
        {
            Visit(change.EntityId);
        }

        return (sorted, cyclicIds);
    }
}

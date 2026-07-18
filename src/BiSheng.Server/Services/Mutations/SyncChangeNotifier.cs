using BiSheng.Server.Hubs;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using Microsoft.AspNetCore.SignalR;

namespace BiSheng.Server.Services.Mutations;

/// <summary>
/// SignalR 变更通知：Commit 后向同用户 Group 推送<strong>轻量元数据</strong>（无 Payload）。
/// 客户端收到通知后通过 HTTP Pull 拉取完整数据（吹哨 / 搬砖分离）。
/// </summary>
public sealed class SyncChangeNotifier : ISyncChangeNotifier
{
    private readonly IHubContext<SyncHub> _hubContext;

    /// <summary>构造 Notifier</summary>
    public SyncChangeNotifier(IHubContext<SyncHub> hubContext)
    {
        _hubContext = hubContext;
    }

    /// <inheritdoc />
    public async Task NotifyAppliedAsync(Guid userId, AppliedMutation applied, CancellationToken ct = default)
    {
        var userIdStr = userId.ToString();
        var mutation = applied.Mutation;

        // 主变更：仅推送元数据，不查库、不带正文
        await _hubContext.NotifyUserChange(userIdStr, BuildNotifyDto(
            mutation.EntityType,
            mutation.EntityId,
            mutation.Action,
            applied.Version,
            mutation.UpdatedAt ?? DateTime.UtcNow));

        // 级联删除的子孙实体（同样无 Payload）
        foreach (var entry in applied.Cascaded)
        {
            await _hubContext.NotifyUserChange(userIdStr, BuildNotifyDto(
                entry.EntityType,
                entry.EntityId,
                ChangeActions.Delete,
                entry.Version,
                entry.UpdatedAt));
        }
    }

    /// <inheritdoc />
    public async Task NotifyBatchAsync(
        Guid userId,
        IEnumerable<AppliedMutation> applied,
        CancellationToken ct = default)
    {
        foreach (var item in applied)
        {
            await NotifyAppliedAsync(userId, item, ct);
        }
    }

    /// <summary>构建无 Payload 的轻量 ChangeDto（约数十字节）</summary>
    private static ChangeDto BuildNotifyDto(
        string entityType,
        Guid entityId,
        string action,
        long version,
        DateTime timestamp)
    {
        return new ChangeDto
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Version = version,
            Timestamp = timestamp,
            Payload = null
        };
    }
}

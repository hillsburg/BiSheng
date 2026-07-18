using BiSheng.Shared.Sync;

namespace BiSheng.Server.Services;

/// <summary>
/// 同步业务服务接口：Pull / Push / 版本查询
/// </summary>
public interface ISyncService
{
    /// <summary>
    /// 获取服务端当前最新版本号，并更新设备同步游标
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="apiKeyId">API Key ID（设备标识）</param>
    /// <param name="ct">取消令牌</param>
    Task<long> GetServerVersionAsync(Guid userId, Guid apiKeyId, CancellationToken ct = default);

    /// <summary>
    /// 拉取 since 版本之后的变更（终态折叠 + 分页）。
    /// since≤0 时导出当前实体快照（忽略 SyncLog 裁剪线），snapshotOffset 为快照分页游标。
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="apiKeyId">API Key ID</param>
    /// <param name="since">客户端已同步到的版本号；≤0 表示实体快照重建</param>
    /// <param name="limit">每批条数；≤0 时使用默认页大小</param>
    /// <param name="snapshotOffset">实体快照分页偏移（仅 since≤0 时有效）</param>
    /// <param name="ct">取消令牌</param>
    Task<SyncPullResponse> PullAsync(
        Guid userId,
        Guid apiKeyId,
        long since,
        int limit = 0,
        long snapshotOffset = 0,
        CancellationToken ct = default);

    /// <summary>
    /// 应用客户端推送的变更批次，返回成功/失败明细
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="apiKeyId">API Key ID</param>
    /// <param name="request">Push 请求体</param>
    /// <param name="ct">取消令牌</param>
    Task<SyncPushResponse> PushAsync(
        Guid userId,
        Guid apiKeyId,
        SyncPushRequest request,
        CancellationToken ct = default);
}

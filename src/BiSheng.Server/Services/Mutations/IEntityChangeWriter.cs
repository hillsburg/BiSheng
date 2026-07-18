using BiSheng.Server.Data;

namespace BiSheng.Server.Services.Mutations;

/// <summary>
/// 实体变更的唯一写入口：校验通过后才 ReserveVersion + 写 SyncLog。
/// 不负责事务、不负责 SignalR；须在调用方已 BeginTransaction 的 DbContext 上调用。
/// </summary>
public interface IEntityChangeWriter
{
    /// <summary>
    /// 应用单条变更。
    /// </summary>
    /// <param name="db">当前事务内的 DbContext</param>
    /// <param name="userId">用户 ID</param>
    /// <param name="mutation">变更描述</param>
    /// <param name="batchContext">Push 批次上下文；REST 单条调用可传 null（使用库内 folder）</param>
    /// <param name="options">写入选项</param>
    /// <param name="ct">取消令牌</param>
    Task<MutationApplyResult> TryApplyAsync(
        AppDbContext db,
        Guid userId,
        EntityMutation mutation,
        MutationBatchContext? batchContext,
        MutationWriteOptions options,
        CancellationToken ct = default);
}

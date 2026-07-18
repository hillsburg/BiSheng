namespace BiSheng.Server.Services.Mutations;

/// <summary>事务 Commit 之后向同用户其他在线客户端推送变更</summary>
public interface ISyncChangeNotifier
{
    /// <summary>通知单条已成功应用的变更（含级联子孙 Delete）</summary>
    Task NotifyAppliedAsync(Guid userId, AppliedMutation applied, CancellationToken ct = default);

    /// <summary>批量通知 Push 批次内已成功应用的变更</summary>
    Task NotifyBatchAsync(Guid userId, IEnumerable<AppliedMutation> applied, CancellationToken ct = default);
}

namespace BiSheng.Server.Services.Mutations;

/// <summary>单条变更的应用结果</summary>
public abstract record MutationApplyResult;

/// <summary>校验通过并已写入 ChangeTracker（尚未 SaveChanges）</summary>
public sealed record MutationApplied(AppliedMutation Applied) : MutationApplyResult;

/// <summary>校验失败，未消耗版本号</summary>
public sealed record MutationSkipped(string Reason) : MutationApplyResult;

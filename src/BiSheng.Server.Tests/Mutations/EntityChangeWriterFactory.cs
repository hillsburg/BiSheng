using BiSheng.Server.Data;
using BiSheng.Server.Services;
using BiSheng.Server.Services.Mutations;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Tests.Mutations;

/// <summary>测试辅助：构造 EntityChangeWriter</summary>
internal static class EntityChangeWriterFactory
{
    /// <summary>用与 SyncService 相同的版本/Revision 依赖构造 Writer</summary>
    public static EntityChangeWriter New() => new(
        new UserSyncVersionService(),
        new NoteRevisionService());
}

/// <summary>在内存库上执行 Writer + SaveChanges 的便捷方法</summary>
internal static class EntityChangeWriterTestHelper
{
    /// <summary>
    /// 构造批次上下文：库内已有 folder + 可选额外 Id
    /// </summary>
    public static async Task<MutationBatchContext> CreateBatchContextAsync(
        AppDbContext db,
        Guid userId,
        CancellationToken ct = default)
    {
        var ids = await db.Folders
            .Where(f => f.UserId == userId && !f.IsDeleted)
            .Select(f => f.Id)
            .ToListAsync(ct);

        return new MutationBatchContext { AvailableFolderIds = ids.ToHashSet() };
    }

    /// <summary>应用单条变更并提交事务</summary>
    public static async Task<MutationApplyResult> ApplyAndSaveAsync(
        AppDbContext db,
        Guid userId,
        EntityMutation mutation,
        MutationBatchContext? batchContext = null,
        CancellationToken ct = default)
    {
        var writer = EntityChangeWriterFactory.New();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await writer.TryApplyAsync(
            db,
            userId,
            mutation,
            batchContext,
            new MutationWriteOptions(),
            ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }
}

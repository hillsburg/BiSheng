using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Data;

/// <summary>
/// EF Core 迁移辅助：兼容 EnsureCreated 时代遗留的数据库，避免重复建表
/// </summary>
public static class DatabaseMigration
{
    /// <summary>
    /// 应用所有待执行迁移。
    /// 若检测到旧版 EnsureCreated 数据库（有 Users 表但无迁移历史），则标记 Initial 迁移为已应用。
    /// </summary>
    /// <param name="db">数据库上下文</param>
    /// <param name="initialMigrationId">Initial 迁移的 MigrationId（与 Migrations 文件夹中文件名一致）</param>
    public static async Task ApplyAsync(AppDbContext db, string initialMigrationId)
    {
        await LegacyMigrationBaseline.EnsureAsync(db, initialMigrationId, "Users");
        await db.Database.MigrateAsync();
    }
}

using Microsoft.EntityFrameworkCore;

namespace BiSheng.Latte.Data;

/// <summary>
/// EF Core 迁移辅助：兼容 EnsureCreated 时代遗留的 local.db
/// </summary>
public static class DatabaseMigration
{
    /// <summary>
    /// 应用所有待执行迁移；legacy 库（有 Folders 表但无迁移历史）自动标记 Initial 为已应用
    /// </summary>
    public static async Task ApplyAsync(LocalDbContext db, string initialMigrationId)
    {
        await LegacyMigrationBaseline.EnsureAsync(db, initialMigrationId, "Folders");
        await db.Database.MigrateAsync();
    }
}

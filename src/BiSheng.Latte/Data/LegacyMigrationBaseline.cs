using System.Data;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Latte.Data;

/// <summary>
/// 将 EnsureCreated 遗留库标记为已应用 Initial 迁移，避免 Migrate 重复建表
/// </summary>
internal static class LegacyMigrationBaseline
{
    public static async Task EnsureAsync(DbContext db, string initialMigrationId, string legacyMarkerTable)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        if (applied.Count > 0)
            return;

        if (!await TableExistsAsync(db, legacyMarkerTable))
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ({0}, {1});
            """,
            initialMigrationId,
            ProductInfo.GetVersion());
    }

    private static async Task<bool> TableExistsAsync(DbContext db, string tableName)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync();
        return result != null;
    }
}

/// <summary>EF Core 产品版本（写入 __EFMigrationsHistory）</summary>
file static class ProductInfo
{
    public static string GetVersion() =>
        typeof(DbContext).Assembly.GetName().Version?.ToString(3) ?? "8.0.11";
}

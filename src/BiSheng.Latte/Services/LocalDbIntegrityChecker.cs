using System.Windows;
using BiSheng.Latte.Data;
using BiSheng.Latte.Models;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Latte.Services;

/// <summary>启动时对 local.db 做 PRAGMA integrity_check</summary>
public static class LocalDbIntegrityChecker
{
    /// <summary>校验数据库并返回结构化结果</summary>
    public static LocalDbIntegrityCheckResult Verify(Func<LocalDbContext> dbFactory)
    {
        try
        {
            using var db = dbFactory();
            var connection = db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                connection.Open();
            }

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check";
            var result = command.ExecuteScalar()?.ToString();

            if (string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                LogHelper.Debug("local.db 完整性检查通过");
                return LocalDbIntegrityCheckResult.Passed();
            }

            var message = result ?? "(null)";
            LogHelper.Error($"local.db 完整性检查失败: {message}");
            return LocalDbIntegrityCheckResult.Failed(message);
        }
        catch (Exception ex)
        {
            LogHelper.Error("local.db 完整性检查异常", ex);
            return LocalDbIntegrityCheckResult.Failed(ex.Message);
        }
    }

    /// <summary>校验数据库；失败时写日志并返回 false</summary>
    public static bool VerifyAndLog(Func<LocalDbContext> dbFactory) =>
        Verify(dbFactory).Ok;
}

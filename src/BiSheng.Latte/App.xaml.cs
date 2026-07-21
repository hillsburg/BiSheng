using System.Windows;
using BiSheng.Latte.Composition;
using BiSheng.Latte.Data;
using BiSheng.Latte.Models;
using BiSheng.Latte.Services;
using Microsoft.Data.Sqlite;

namespace BiSheng.Latte;

public partial class App : Application
{
    /// <summary>启动时 local.db 完整性检查是否失败</summary>
    public static bool StartupIntegrityFailed { get; private set; }

    /// <summary>启动完整性失败详情</summary>
    public static string StartupIntegrityMessage { get; private set; } = string.Empty;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // 托盘驻留时隐藏主窗口不应退出进程
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 先迁移 exe 旁旧数据，再初始化日志与打开数据库
        LatteLegacyMigrationResult? migration = null;
        Exception? migrationError = null;
        try
        {
            migration = LatteLegacyDataMigrator.MigrateIfNeeded();
        }
        catch (Exception ex)
        {
            migrationError = ex;
        }

        LogHelper.Initialize();
        LogHelper.Info("应用启动");
        if (migrationError != null)
        {
            LogHelper.Error("从 exe 旁迁移用户数据失败，将继续使用 LocalAppData 路径", migrationError);
        }
        else if (migration?.HadWork == true)
        {
            LogHelper.Info(migration.Summary);
        }

        LatteHost.Build();

        var dbFactory = LatteHost.GetRequiredService<Func<LocalDbContext>>();

        using (var db = dbFactory())
        {
            await DatabaseMigration.ApplyAsync(db, MigrationIds.Initial);
            db.InitializeWalMode();
        }

        var integrity = LocalDbIntegrityChecker.Verify(dbFactory);
        StartupIntegrityFailed = !integrity.Ok;
        StartupIntegrityMessage = integrity.Message;
        SqliteConnection.ClearAllPools();
        LocalDatabaseBackupService.TryRunScheduledBackup(DataSafetySettings.Load(), onExit: false);

        LatteHost.GetRequiredService<LocalEditJournalService>().PruneIfNeeded();
        LatteHost.GetRequiredService<SyncConflictCleanupService>().PruneIfNeeded();
        LatteHost.GetRequiredService<TrashService>().PurgeExpired();

        LogHelper.Debug("本地数据库已就绪");

        var tray = new TrayIconService();
        tray.Initialize();

        var mainWindow = new MainWindow(LatteHost.GetRequiredService<ViewModels.MainViewModel>());
        mainWindow.AttachTray(tray);
        MainWindow = mainWindow;
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogHelper.Info("应用退出");
        try
        {
            LatteHost.Dispose();
        }
        catch (Exception ex)
        {
            LogHelper.Error("释放应用服务失败", ex);
        }
        finally
        {
            LogHelper.Shutdown();
            base.OnExit(e);
        }
    }
}

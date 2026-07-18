using System.Windows;
using BiSheng.Latte.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace BiSheng.Latte.Tests.Fixtures;

/// <summary>
/// 每个测试独立 in-memory SQLite；设置 LocalDbContext.TestOptions 供无参构造使用
/// </summary>
public sealed class LatteTestDbFactory : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>当前测试 DbContext（与 App 内 new LocalDbContext() 共用 TestOptions）</summary>
    public LocalDbContext Db { get; }

    public LatteTestDbFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        LocalDbContext.TestConnection = _connection;
        Db = new LocalDbContext();
        Db.Database.EnsureCreated();
    }

    /// <summary>确保 WPF Application 存在（EditorViewModel 等依赖 Dispatcher）</summary>
    public static void EnsureWpfApplication()
    {
        if (Application.Current == null)
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }
    }

    public void Dispose()
    {
        Db.Dispose();
        LocalDbContext.TestConnection = null;
        _connection.Dispose();
    }
}

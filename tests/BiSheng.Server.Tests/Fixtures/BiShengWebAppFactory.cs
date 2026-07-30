using System.Security.Cryptography;
using System.Text;
using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BiSheng.Server.Tests.Fixtures;

/// <summary>
/// WebApplicationFactory 集成测试宿主：
/// 用 in-memory SQLite 替换生产 DbContext，移除后台 HostedService 避免干扰
/// </summary>
public sealed class BiShengWebAppFactory : WebApplicationFactory<Program>
{
    private SqliteConnection _connection = null!;
    private readonly Action<IServiceCollection>? _configureTestServices;

    /// <summary>共享的 in-memory 连接，工厂与种子 DbContext 共用</summary>
    public SqliteConnection Connection => _connection;

    /// <summary>默认测试宿主</summary>
    public BiShengWebAppFactory()
    {
    }

    /// <summary>附加额外测试服务配置（如覆盖 CompatibilityOptions）</summary>
    public BiShengWebAppFactory(Action<IServiceCollection> configureTestServices)
    {
        _configureTestServices = configureTestServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        builder.UseEnvironment("Test");

        builder.ConfigureTestServices(services =>
        {
            // 移除生产 DbContext 注册与后台 HostedService，替换为指向 in-memory 连接的实例
            var toRemove = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                || d.ServiceType == typeof(AppDbContext)
                || d.ServiceType == typeof(IHostedService)).ToList();
            foreach (var d in toRemove)
            {
                services.Remove(d);
            }

            services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(_connection));
            _configureTestServices?.Invoke(services);
        });
    }

    /// <summary>
    /// 种子：管理员标记 + 用户 + ApiKey（哈希存储）+ 根文件夹 + UserSyncMeta
    /// </summary>
    /// <param name="currentVersion">UserSyncMeta 初始版本号</param>
    /// <returns>plaintextApiKey / userId / folderId</returns>
    public async Task<(string plaintextApiKey, Guid userId, Guid folderId)> SeedAsync(
        long currentVersion = 10)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 标记服务已初始化，让 SetupMiddleware 放行
        if (!await db.ServerConfigs.AnyAsync(c => c.Id == 1))
        {
            db.ServerConfigs.Add(new ServerConfig { Id = 1, IsSetup = true, SetupAt = DateTime.UtcNow });
        }

        var userId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var plaintextApiKey = "test-key-" + Guid.NewGuid().ToString("N");

        db.Users.Add(new User
        {
            Id = userId,
            Username = "u",
            PasswordHash = "x",
            TotpSecret = "x"
        });
        db.ApiKeys.Add(new ApiKey
        {
            Id = apiKeyId,
            UserId = userId,
            KeyValue = HashApiKey(plaintextApiKey),
            IsActive = true
        });
        db.Folders.Add(new Folder
        {
            Id = folderId,
            UserId = userId,
            Name = "F1",
            Version = currentVersion
        });
        db.UserSyncMetas.Add(new UserSyncMeta
        {
            UserId = userId,
            CurrentVersion = currentVersion
        });
        await db.SaveChangesAsync();
        return (plaintextApiKey, userId, folderId);
    }

    /// <summary>取一个新的种子 DbContext（与工厂共享同一 in-memory 连接）</summary>
    public AppDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>复制 ApiKeyAuthHandler.HashApiKey 的 SHA256 → hex 小写算法</summary>
    private static string HashApiKey(string apiKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connection?.Dispose();
        }
        base.Dispose(disposing);
    }
}

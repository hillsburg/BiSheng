using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BiSheng.Server.Data;

/// <summary>EF Core 设计时工厂（dotnet ef migrations 使用）</summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(ServerDatabasePaths.DefaultConnectionString)
            .Options;
        return new AppDbContext(options);
    }
}

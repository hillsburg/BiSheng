using BiSheng.Server.Auth;
using BiSheng.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BiSheng.Server.Tests.Auth;

public class AuthMigrationTests
{
    [Fact]
    public async Task UpgradePreservesExistingUserAndBusinessRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20260721100620_AddApiKeyLastUsedAt");
        var id = Guid.NewGuid().ToString().ToUpperInvariant();
        var folderId = Guid.NewGuid().ToString().ToUpperInvariant();
        var hash = PasswordCredentialService.HashPassword("Password1!");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Users (Id,Username,PasswordHash,TotpSecret,CreatedAt) VALUES ({id},'admin',{hash},'existing-secret','2026-07-01')");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Folders (Id,UserId,Name,IsDeleted,Version,CreatedAt,UpdatedAt) VALUES ({folderId},{id},'existing-folder',0,12,'2026-07-01','2026-07-01')");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ApiKeys (Id,UserId,KeyValue,DeviceName,IsActive,CreatedAt) VALUES ({Guid.NewGuid().ToString().ToUpperInvariant()},{id},'existing-key','device',1,'2026-07-01')");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Notes (Id,UserId,FolderId,Title,Content,IsFavorite,IsPinned,IsDeleted,Version,CreatedAt,UpdatedAt) VALUES ({Guid.NewGuid().ToString().ToUpperInvariant()},{id},{folderId},'title','existing-content',0,0,0,13,'2026-07-01','2026-07-01')");
        await db.Database.MigrateAsync();
        var user = await db.Users.SingleAsync();
        Assert.Equal(Guid.Parse(id), user.Id);
        Assert.Equal(hash, user.PasswordHash);
        Assert.Equal("existing-secret", user.TotpSecret);
        Assert.Equal(1, user.AuthVersion);
        Assert.Null(user.LastTotpTimeStep);
        Assert.Equal("existing-folder", (await db.Folders.SingleAsync()).Name);
        Assert.Equal("existing-content", (await db.Notes.SingleAsync()).Content);
        Assert.Equal("existing-key", (await db.ApiKeys.SingleAsync()).KeyValue);
        Assert.True((await db.ApiKeys.SingleAsync()).IsActive);
    }
}

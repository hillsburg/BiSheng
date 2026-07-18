using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Tests.Fixtures;

/// <summary>
/// 每个测试一个独立 in-memory SQLite，互不污染；
/// 保持 SqliteConnection 打开直到 Dispose，否则 in-memory 库会消失
/// </summary>
public sealed class TestDbFactory : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>当前测试用的 DbContext</summary>
    public AppDbContext Db { get; }

    public TestDbFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new AppDbContext(options);
        Db.Database.EnsureCreated();
    }

    /// <summary>
    /// 种子：一个用户 + 一个 ApiKey + 一个根文件夹 + 一篇笔记（version=10）+ UserSyncMeta
    /// </summary>
    /// <param name="noteContent">种子笔记正文</param>
    /// <returns>userId / apiKeyId / folderId / noteId</returns>
    public (Guid userId, Guid apiKeyId, Guid folderId, Guid noteId) SeedUserWithNote(
        string noteContent = "base")
    {
        var userId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var noteId = Guid.NewGuid();

        Db.Users.Add(new User
        {
            Id = userId,
            Username = "u",
            PasswordHash = "x",
            TotpSecret = "x"
        });
        Db.ApiKeys.Add(new ApiKey
        {
            Id = apiKeyId,
            UserId = userId,
            KeyValue = "k"
        });
        Db.Folders.Add(new Folder
        {
            Id = folderId,
            UserId = userId,
            Name = "F1",
            Version = 10
        });
        Db.Notes.Add(new Note
        {
            Id = noteId,
            UserId = userId,
            FolderId = folderId,
            Title = "N1",
            Content = noteContent,
            Version = 10
        });
        Db.UserSyncMetas.Add(new UserSyncMeta
        {
            UserId = userId,
            CurrentVersion = 10
        });
        Db.SaveChanges();
        return (userId, apiKeyId, folderId, noteId);
    }

    /// <summary>与当前内存库共用连接的新 DbContext（用于事务回滚后独立校验）</summary>
    public AppDbContext CreateSiblingContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new AppDbContext(options);
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}

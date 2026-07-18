using System.Net;
using System.Net.Http.Json;
using BiSheng.Server.Data.Entities;
using BiSheng.Server.Tests.Fixtures;
using BiSheng.Shared;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Tests.Controllers;

/// <summary>
/// C 项端到端：REST 写路径事务原子性
/// 验证 NotesController.CreateNote 的版本预留 + 实体写入 + SyncLog 在同一事务内，
/// 失败时 CurrentVersion 一并回滚，不会产生版本空洞
/// </summary>
public class NotesControllerE2ETests : IAsyncLifetime
{
    private BiShengWebAppFactory _factory = null!;
    private HttpClient _client = null!;
    private string _apiKey = null!;
    private Guid _userId;
    private Guid _folderId;

    public async Task InitializeAsync()
    {
        _factory = new BiShengWebAppFactory();
        _client = _factory.CreateClient();
        (_apiKey, _userId, _folderId) = await _factory.SeedAsync(currentVersion: 10);
        _client.DefaultRequestHeaders.Add("X-Api-Key", _apiKey);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>成功创建：Note + SyncLog 原子落库，CurrentVersion 推进到 11</summary>
    [Fact]
    public async Task CreateNote_Success_AdvancesVersionAtomically()
    {
        var resp = await _client.PostAsJsonAsync("/api/notes", new
        {
            title = "NewNote",
            content = "hello",
            folderId = _folderId
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        await using var db = _factory.NewDbContext();
        var note = await db.Notes.SingleAsync(n => n.UserId == _userId && n.Title == "NewNote");
        Assert.Equal(11, note.Version);

        var syncLog = await db.SyncLogs.SingleAsync(s => s.UserId == _userId && s.EntityId == note.Id);
        Assert.Equal(ChangeActions.Create, syncLog.Action);
        Assert.Equal(11, syncLog.Version);

        Assert.Equal(11, await db.UserSyncMetas
            .Where(m => m.UserId == _userId)
            .Select(m => m.CurrentVersion)
            .SingleAsync());
    }

    /// <summary>无效 folderId：控制器在事务前返回 400，不消耗版本号</summary>
    [Fact]
    public async Task CreateNote_InvalidFolder_BadRequest_NoVersionConsumed()
    {
        var ghostFolderId = Guid.NewGuid();

        var resp = await _client.PostAsJsonAsync("/api/notes", new
        {
            title = "Orphan",
            content = "x",
            folderId = ghostFolderId
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        await using var db = _factory.NewDbContext();
        // 版本号未前进，无新实体
        Assert.Equal(10, await db.UserSyncMetas
            .Where(m => m.UserId == _userId)
            .Select(m => m.CurrentVersion)
            .SingleAsync());
        Assert.False(await db.Notes.AnyAsync(n => n.UserId == _userId && n.Title == "Orphan"));
        Assert.False(await db.SyncLogs.AnyAsync(s => s.UserId == _userId && s.Version == 11));
    }

    /// <summary>
    /// 事务中 SaveChanges 失败（SyncLog 唯一约束冲突）→ 500 + CurrentVersion 回滚到 10
    /// 这是 C 项的核心保证：版本预留与实体写入必须同事务，失败不留版本空洞
    /// </summary>
    [Fact]
    public async Task CreateNote_SaveChangesFails_RollsBackVersion()
    {
        // 预占 v11：让 ReserveNextVersion 返回 11 后 SaveChanges 唯一约束冲突
        await using var seedDb = _factory.NewDbContext();
        seedDb.SyncLogs.Add(new SyncLog
        {
            EntityType = EntityTypes.Note,
            EntityId = Guid.NewGuid(),
            Action = ChangeActions.Update,
            Version = 11,
            UserId = _userId,
            Timestamp = DateTime.UtcNow
        });
        await seedDb.SaveChangesAsync();

        var resp = await _client.PostAsJsonAsync("/api/notes", new
        {
            title = "WillRollback",
            content = "x",
            folderId = _folderId
        });

        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);

        await using var db = _factory.NewDbContext();
        // CurrentVersion 未前进：UPDATE … RETURNING 在事务内，SaveChanges 失败后整体回滚
        Assert.Equal(10, await db.UserSyncMetas
            .Where(m => m.UserId == _userId)
            .Select(m => m.CurrentVersion)
            .SingleAsync());
        // 实体未落库
        Assert.False(await db.Notes.AnyAsync(n => n.UserId == _userId && n.Title == "WillRollback"));
    }
}

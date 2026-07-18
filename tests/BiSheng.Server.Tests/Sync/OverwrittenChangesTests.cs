using BiSheng.Server.Services;
using BiSheng.Server.Tests.Fixtures;
using BiSheng.Shared;
using BiSheng.Shared.Sync;

namespace BiSheng.Server.Tests.Sync;

/// <summary>
/// E：冲突盲区——客户端 Push 覆盖远端编辑时，响应应包含被覆盖的远端 pre-state
/// </summary>
public class OverwrittenChangesTests
{
    /// <summary>
    /// 设备 B 在 A 离线期间改了 N1 为 "remote"（v11）；
    /// 设备 A 以 clientVersion=10 推送 N1 Update 为 "local"（成功，v12）。
    /// 响应的 OverwrittenChanges 必须包含 N1 的远端 pre-state（"remote"），
    /// 且 N1 当前 DB 内容是 "local"（A 的版本胜出，不回滚）
    /// </summary>
    [Fact]
    public async Task Push_OverwritesRemoteChange_ReturnsOverwrittenPreState()
    {
        using var fixture = new TestDbFactory();
        var (userId, apiKeyId, folderId, noteId) = fixture.SeedUserWithNote("base");
        var sync = SyncServiceFactory.New(fixture.Db);

        // ① 设备 B：改 N1 为 "remote"，CurrentVersion 推到 11
        await sync.PushAsync(userId, apiKeyId, new SyncPushRequest
        {
            ClientVersion = 10,
            Changes = new()
            {
                new ClientChangeDto
                {
                    EntityType = EntityTypes.Note,
                    EntityId = noteId,
                    Action = ChangeActions.Update,
                    Payload = $$"""{"title":"N1","content":"remote","folderId":"{{folderId}}","isFavorite":false,"isPinned":false}""",
                    UpdatedAt = DateTime.UtcNow
                }
            }
        }, CancellationToken.None);

        // ② 设备 A：clientVersion=10（不知道 B 改过），推 N1 Update 为 "local"
        var resp = await sync.PushAsync(userId, apiKeyId, new SyncPushRequest
        {
            ClientVersion = 10,
            Changes = new()
            {
                new ClientChangeDto
                {
                    EntityType = EntityTypes.Note,
                    EntityId = noteId,
                    Action = ChangeActions.Update,
                    Payload = $$"""{"title":"N1","content":"local","folderId":"{{folderId}}","isFavorite":false,"isPinned":false}""",
                    UpdatedAt = DateTime.UtcNow
                }
            }
        }, CancellationToken.None);

        // ③ OverwrittenChanges 包含 N1 的远端 pre-state "remote"
        Assert.True(resp.Success);
        var overwritten = Assert.Single(resp.OverwrittenChanges);
        Assert.Equal(noteId, overwritten.EntityId);
        Assert.Equal(ChangeActions.Update, overwritten.Action);
        Assert.Contains("remote", overwritten.Payload);
        // 远端 pre-state 的版本是 11（被覆盖的那次）
        Assert.Equal(11, overwritten.Version);

        // ④ N1 当前 DB 内容是 "local"（A 的版本胜出，不回滚）
        var note = await fixture.Db.Notes.FindAsync(noteId);
        Assert.Equal("local", note!.Content);

        // ⑤ ConflictingChanges 不含 N1（N1 在 appliedEntityIds，走 Overwritten 而非 Conflicting）
        Assert.DoesNotContain(resp.ConflictingChanges, c => c.EntityId == noteId);
    }
}

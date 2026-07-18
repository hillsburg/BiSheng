using BiSheng.Server.Data.Entities;
using BiSheng.Server.Services;
using BiSheng.Server.Tests.Fixtures;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Tests.Sync;

/// <summary>
/// A2 核心约束：部分失败时 response.ServerVersion 是"已确认应用"水位，
/// ConflictingChanges 完整覆盖 [clientVersion, serverVersion] 区间内未在本批 applied 的远程变更
/// </summary>
public class PushPartialFailureTests
{
    /// <summary>
    /// 设备 B 在 A 离线期间改了 N1（v11）；A 推两条变更：N2 成功、N3 FK 失败；
    /// 响应必须返回 ServerVersion=已确认水位、FailedEntityIds=[N3]、ConflictingChanges=[N1 remote]
    /// </summary>
    [Fact]
    public async Task Push_PartialFailure_WithRemoteConflict_ReturnsConfirmedVersionAndRemoteChanges()
    {
        using var fixture = new TestDbFactory();
        var (userId, apiKeyId, folderId, noteId) = fixture.SeedUserWithNote("base");
        var sync = SyncServiceFactory.New(fixture.Db);

        // ① 模拟设备 B：改 N1 为 "remote"，CurrentVersion 推到 11
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

        // ② 设备 A：clientVersion=10，推两条变更
        var n2Id = Guid.NewGuid();
        var n3Id = Guid.NewGuid();
        var ghostFolderId = Guid.NewGuid();

        var resp = await sync.PushAsync(userId, apiKeyId, new SyncPushRequest
        {
            ClientVersion = 10,
            Changes = new()
            {
                new ClientChangeDto
                {
                    EntityType = EntityTypes.Note,
                    EntityId = n2Id,
                    Action = ChangeActions.Create,
                    Payload = $$"""{"title":"N2","content":"x","folderId":"{{folderId}}","isFavorite":false,"isPinned":false}"""
                },
                new ClientChangeDto
                {
                    EntityType = EntityTypes.Note,
                    EntityId = n3Id,
                    Action = ChangeActions.Create,
                    Payload = $$"""{"title":"N3","content":"x","folderId":"{{ghostFolderId}}","isFavorite":false,"isPinned":false}"""
                }
            }
        }, CancellationToken.None);

        // ③ 响应断言
        Assert.False(resp.Success);
        Assert.False(resp.TransactionRolledBack);
        Assert.Equal(new[] { n3Id }, resp.FailedEntityIds);
        Assert.Equal(12, resp.ServerVersion);   // N2 分配的版本 = 已确认水位

        // ConflictingChanges 必须包含 N1 (v11 "remote")，因为 N1 不在本批 appliedEntityIds={n2Id}
        var conflict = Assert.Single(resp.ConflictingChanges);
        Assert.Equal(noteId, conflict.EntityId);
        Assert.Equal(ChangeActions.Update, conflict.Action);
        Assert.Contains("remote", conflict.Payload);

        // ④ DB 状态断言
        Assert.NotNull(await fixture.Db.Notes.FindAsync(n2Id));      // 成功项落库
        Assert.Null(await fixture.Db.Notes.FindAsync(n3Id));         // 失败项未落库
        Assert.Equal("remote", (await fixture.Db.Notes.FindAsync(noteId))!.Content); // 未被本批覆盖

        // ⑤ 版本计数器与设备游标：游标仅对齐 ClientVersion，不抬到 tip
        Assert.Equal(12, await fixture.Db.UserSyncMetas
            .Where(m => m.UserId == userId)
            .Select(m => m.CurrentVersion)
            .SingleAsync(CancellationToken.None));

        var state = await fixture.Db.ClientSyncStates
            .SingleAsync(s => s.ApiKeyId == apiKeyId, CancellationToken.None);
        Assert.Equal(10, state.LastSyncVersion);
    }

    /// <summary>
    /// 全部成功 + 无远端冲突：ServerVersion=本批最高版本，无 ConflictingChanges
    /// </summary>
    [Fact]
    public async Task Push_AllSucceeded_NoConflicts_AdvancesCursor()
    {
        using var fixture = new TestDbFactory();
        var (userId, apiKeyId, folderId, _) = fixture.SeedUserWithNote();
        var sync = SyncServiceFactory.New(fixture.Db);

        var newNoteId = Guid.NewGuid();
        var resp = await sync.PushAsync(userId, apiKeyId, new SyncPushRequest
        {
            ClientVersion = 10,
            Changes = new()
            {
                new ClientChangeDto
                {
                    EntityType = EntityTypes.Note,
                    EntityId = newNoteId,
                    Action = ChangeActions.Create,
                    Payload = $$"""{"title":"New","content":"c","folderId":"{{folderId}}","isFavorite":false,"isPinned":false}"""
                }
            }
        }, CancellationToken.None);

        Assert.True(resp.Success);
        Assert.Empty(resp.FailedEntityIds);
        Assert.Empty(resp.ConflictingChanges);
        Assert.Equal(11, resp.ServerVersion);

        var state = await fixture.Db.ClientSyncStates
            .SingleAsync(s => s.ApiKeyId == apiKeyId, CancellationToken.None);
        Assert.Equal(10, state.LastSyncVersion);
    }
}

using BiSheng.Server.Services.Mutations;
using BiSheng.Server.Tests.Fixtures;
using BiSheng.Server.Tests.Sync;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Tests.Mutations;

/// <summary>
/// PR-5：REST 与 Sync Push 经同一 Writer 后，SyncLog / 级联 / 失败语义一致
/// </summary>
public class RestSyncParityTests
{
    /// <summary>同 payload 创建 Note：REST 与 Push 的 SyncLog Action/Version/Payload 形态一致</summary>
    [Fact]
    public async Task Note_Create_ViaRest_MatchesPush_SyncLogShape()
    {
        using var pushFixture = new TestDbFactory();
        using var restFixture = new TestDbFactory();
        var (userIdP, apiKeyId, folderIdP, _) = pushFixture.SeedUserWithNote();
        var (userIdR, _, folderIdR, _) = restFixture.SeedUserWithNote();

        const string title = "ParityNote";
        const string content = "same-body";
        var noteId = Guid.NewGuid();

        var pushResp = await MutationTestHelper.PushCreateNoteAsync(
            pushFixture.Db, userIdP, apiKeyId, noteId, folderIdP, title, content);
        Assert.True(pushResp.Success);

        var restResult = await MutationTestHelper.RestCreateNoteAsync(
            restFixture.Db, userIdR, folderIdR, title, content);
        Assert.Equal(MutationOutcome.Success, restResult.Outcome);

        var pushLog = await pushFixture.Db.SyncLogs
            .SingleAsync(s => s.EntityId == noteId && s.Action == ChangeActions.Create);
        var restNote = await restFixture.Db.Notes.SingleAsync(n => n.Title == title);
        var restLog = await restFixture.Db.SyncLogs
            .SingleAsync(s => s.EntityId == restNote.Id && s.Action == ChangeActions.Create);

        SyncLogParityAssert.AssertNoteCreateShape(pushLog, 11, folderIdP, title, content);
        SyncLogParityAssert.AssertNoteCreateShape(restLog, 11, folderIdR, title, content);
        SyncLogParityAssert.AssertNoteCreateLogsEquivalent(pushLog, restLog);
    }

    /// <summary>无效 folderId：REST 400 + Push FailedEntityIds，均不消耗版本号</summary>
    [Fact]
    public async Task Note_Create_InvalidFolder_BothPathsRejectAndNoVersionConsumed()
    {
        using var pushFixture = new TestDbFactory();
        using var restFixture = new TestDbFactory();
        var (userIdP, apiKeyId, folderIdP, _) = pushFixture.SeedUserWithNote();
        var (userIdR, _, _, _) = restFixture.SeedUserWithNote();

        var ghostFolderId = Guid.NewGuid();
        var noteId = Guid.NewGuid();

        var pushResp = await MutationTestHelper.PushCreateNoteAsync(
            pushFixture.Db, userIdP, apiKeyId, noteId, ghostFolderId, "X", "x");
        Assert.False(pushResp.Success);
        Assert.Contains(noteId, pushResp.FailedEntityIds);
        Assert.Equal(10, await pushFixture.Db.UserSyncMetas
            .Where(m => m.UserId == userIdP)
            .Select(m => m.CurrentVersion)
            .SingleAsync());
        Assert.False(await pushFixture.Db.Notes.AnyAsync(n => n.Id == noteId));

        var restResult = await MutationTestHelper.RestCreateNoteAsync(
            restFixture.Db, userIdR, ghostFolderId, "X", "x");
        Assert.Equal(MutationOutcome.BadRequest, restResult.Outcome);
        Assert.Equal(10, await restFixture.Db.UserSyncMetas
            .Where(m => m.UserId == userIdR)
            .Select(m => m.CurrentVersion)
            .SingleAsync());
        Assert.False(await restFixture.Db.Notes.AnyAsync(n => n.UserId == userIdR && n.Title == "X"));
    }

    /// <summary>Delete Folder：REST 与 Push 级联软删相同数量实体，SyncLog 条数与版本序列一致</summary>
    [Fact]
    public async Task Folder_Delete_BothPaths_CascadeSameEntitySet()
    {
        using var pushFixture = new TestDbFactory();
        using var restFixture = new TestDbFactory();
        var (userIdP, apiKeyId, _, _) = pushFixture.SeedUserWithNote();
        var (userIdR, _, _, _) = restFixture.SeedUserWithNote();

        var pushTree = MutationTestHelper.SeedCascadeTree(pushFixture.Db, userIdP);
        var restTree = MutationTestHelper.SeedCascadeTree(restFixture.Db, userIdR);

        var pushResp = await MutationTestHelper.PushDeleteFolderAsync(
            pushFixture.Db, userIdP, apiKeyId, pushTree.f1);
        Assert.True(pushResp.Success);

        var restResult = await MutationTestHelper.RestDeleteFolderAsync(
            restFixture.Db, userIdR, restTree.f1);
        Assert.Equal(MutationOutcome.Success, restResult.Outcome);

        await SyncLogParityAssert.AssertFolderDeleteCascadeShapeAsync(
            pushFixture.Db, userIdP,
            pushTree.f1, pushTree.f2, pushTree.f3, pushTree.n1, pushTree.n2, pushTree.n3);

        await SyncLogParityAssert.AssertFolderDeleteCascadeShapeAsync(
            restFixture.Db, userIdR,
            restTree.f1, restTree.f2, restTree.f3, restTree.n1, restTree.n2, restTree.n3);
    }

    /// <summary>Push 部分失败仍走 Writer：无效 FK 失败项不落库，成功项正常分配版本（A2 回归）</summary>
    [Fact]
    public async Task Push_PartialFailure_StillUsesWriter_NoVersionHoleOnSuccess()
    {
        using var fixture = new TestDbFactory();
        var (userId, apiKeyId, folderId, _) = fixture.SeedUserWithNote();

        var okId = Guid.NewGuid();
        var failId = Guid.NewGuid();
        var ghostFolder = Guid.NewGuid();

        var sync = SyncServiceFactory.New(fixture.Db);
        var resp = await sync.PushAsync(userId, apiKeyId, new SyncPushRequest
        {
            ClientVersion = 10,
            Changes =
            {
                new ClientChangeDto
                {
                    EntityType = EntityTypes.Note,
                    EntityId = okId,
                    Action = ChangeActions.Create,
                    Payload = MutationTestHelper.NoteCreatePayload(folderId, "Ok", "ok"),
                    UpdatedAt = DateTime.UtcNow
                },
                new ClientChangeDto
                {
                    EntityType = EntityTypes.Note,
                    EntityId = failId,
                    Action = ChangeActions.Create,
                    Payload = MutationTestHelper.NoteCreatePayload(ghostFolder, "Fail", "x"),
                    UpdatedAt = DateTime.UtcNow
                }
            }
        });

        Assert.False(resp.Success);
        Assert.Contains(failId, resp.FailedEntityIds);
        Assert.Equal(11, resp.ServerVersion);
        Assert.NotNull(await fixture.Db.Notes.FindAsync(okId));
        Assert.Null(await fixture.Db.Notes.FindAsync(failId));

        var okLog = await fixture.Db.SyncLogs
            .SingleAsync(s => s.EntityId == okId && s.Action == ChangeActions.Create);
        Assert.Equal(11, okLog.Version);
    }
}

using System.Text.Json;
using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using BiSheng.Server.DTOs;
using BiSheng.Server.Services.Mutations;
using BiSheng.Server.Tests.Sync;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Tests.Mutations;

/// <summary>REST / Push 双路径测试辅助</summary>
internal static class MutationTestHelper
{
    /// <summary>与 REST CreateNote 等价的 Push Create payload</summary>
    public static string NoteCreatePayload(Guid folderId, string title, string content) =>
        SyncPayloadJson.Serialize(SyncPayloadBuilder.Note(title, content, folderId));

    /// <summary>通过 Sync Push 创建笔记</summary>
    public static Task<SyncPushResponse> PushCreateNoteAsync(
        AppDbContext db,
        Guid userId,
        Guid apiKeyId,
        Guid noteId,
        Guid folderId,
        string title,
        string content,
        long clientVersion = 10,
        CancellationToken ct = default)
    {
        var sync = SyncServiceFactory.New(db);
        return sync.PushAsync(userId, apiKeyId, new SyncPushRequest
        {
            ClientVersion = clientVersion,
            Changes =
            {
                new ClientChangeDto
                {
                    EntityType = EntityTypes.Note,
                    EntityId = noteId,
                    Action = ChangeActions.Create,
                    Payload = NoteCreatePayload(folderId, title, content),
                    UpdatedAt = DateTime.UtcNow
                }
            }
        }, ct);
    }

    /// <summary>通过 NoteMutationService 创建笔记</summary>
    public static Task<NoteMutationResult> RestCreateNoteAsync(
        AppDbContext db,
        Guid userId,
        Guid folderId,
        string title,
        string content,
        CancellationToken ct = default)
    {
        var service = MutationServiceFactory.NewNoteService(db);
        return service.CreateAsync(userId, new CreateNoteRequest
        {
            Title = title,
            Content = content,
            FolderId = folderId
        }, ct);
    }

    /// <summary>通过 Sync Push 删除文件夹</summary>
    public static Task<SyncPushResponse> PushDeleteFolderAsync(
        AppDbContext db,
        Guid userId,
        Guid apiKeyId,
        Guid folderId,
        long clientVersion = 10,
        CancellationToken ct = default)
    {
        var sync = SyncServiceFactory.New(db);
        return sync.PushAsync(userId, apiKeyId, new SyncPushRequest
        {
            ClientVersion = clientVersion,
            Changes =
            {
                new ClientChangeDto
                {
                    EntityType = EntityTypes.Folder,
                    EntityId = folderId,
                    Action = ChangeActions.Delete,
                    UpdatedAt = DateTime.UtcNow
                }
            }
        }, ct);
    }

    /// <summary>通过 FolderMutationService 删除文件夹</summary>
    public static Task<FolderMutationResult> RestDeleteFolderAsync(
        AppDbContext db,
        Guid userId,
        Guid folderId,
        CancellationToken ct = default)
    {
        var service = MutationServiceFactory.NewFolderService(db);
        return service.DeleteAsync(userId, folderId, ct);
    }

    /// <summary>构造 F1 ← F2 ← F3 三层 folder + 各层一篇 note（version=10）</summary>
    public static (Guid f1, Guid f2, Guid f3, Guid n1, Guid n2, Guid n3) SeedCascadeTree(
        AppDbContext db,
        Guid userId)
    {
        var f1 = Guid.NewGuid();
        var f2 = Guid.NewGuid();
        var f3 = Guid.NewGuid();
        var n1 = Guid.NewGuid();
        var n2 = Guid.NewGuid();
        var n3 = Guid.NewGuid();

        db.Folders.Add(new Folder { Id = f1, UserId = userId, Name = "F1", Version = 10 });
        db.Folders.Add(new Folder { Id = f2, UserId = userId, Name = "F2", ParentId = f1, Version = 10 });
        db.Folders.Add(new Folder { Id = f3, UserId = userId, Name = "F3", ParentId = f2, Version = 10 });
        db.Notes.Add(new Note { Id = n1, UserId = userId, FolderId = f1, Title = "N1", Content = "c", Version = 10 });
        db.Notes.Add(new Note { Id = n2, UserId = userId, FolderId = f2, Title = "N2", Content = "c", Version = 10 });
        db.Notes.Add(new Note { Id = n3, UserId = userId, FolderId = f3, Title = "N3", Content = "c", Version = 10 });
        db.SaveChanges();

        return (f1, f2, f3, n1, n2, n3);
    }
}

/// <summary>SyncLog 形态断言（REST 与 Push 共用 Writer 后应一致）</summary>
internal static class SyncLogParityAssert
{
    /// <summary>断言 Note Create 的 SyncLog 字段形态</summary>
    public static void AssertNoteCreateShape(
        SyncLog log,
        long expectedVersion,
        Guid folderId,
        string title,
        string content)
    {
        Assert.Equal(ChangeActions.Create, log.Action);
        Assert.Equal(EntityTypes.Note, log.EntityType);
        Assert.Equal(expectedVersion, log.Version);
        Assert.NotNull(log.Payload);

        var payload = JsonSerializer.Deserialize<NoteChangePayload>(log.Payload!, SyncPayloadJson.Options);
        Assert.NotNull(payload);
        Assert.Equal(title, payload!.Title);
        Assert.Equal(content, payload.Content);
        Assert.Equal(folderId, payload.FolderId);
        Assert.False(payload.IsFavorite);
        Assert.False(payload.IsPinned);
    }

    /// <summary>两条 Note Create SyncLog 的 Action/Version/Payload 语义等价</summary>
    public static void AssertNoteCreateLogsEquivalent(SyncLog pushLog, SyncLog restLog)
    {
        Assert.Equal(pushLog.Action, restLog.Action);
        Assert.Equal(pushLog.EntityType, restLog.EntityType);
        Assert.Equal(pushLog.Version, restLog.Version);

        var pushPayload = JsonSerializer.Deserialize<NoteChangePayload>(pushLog.Payload!, SyncPayloadJson.Options);
        var restPayload = JsonSerializer.Deserialize<NoteChangePayload>(restLog.Payload!, SyncPayloadJson.Options);
        Assert.NotNull(pushPayload);
        Assert.NotNull(restPayload);
        Assert.Equal(pushPayload!.Title, restPayload!.Title);
        Assert.Equal(pushPayload.Content, restPayload.Content);
        Assert.Equal(pushPayload.IsFavorite, restPayload.IsFavorite);
        Assert.Equal(pushPayload.IsPinned, restPayload.IsPinned);
    }

    /// <summary>级联删除后 DB 形态：6 个 Delete SyncLog，版本 11..16</summary>
    public static async Task AssertFolderDeleteCascadeShapeAsync(
        AppDbContext db,
        Guid userId,
        Guid f1,
        Guid f2,
        Guid f3,
        Guid n1,
        Guid n2,
        Guid n3)
    {
        Assert.True((await db.Folders.FindAsync(f1))!.IsDeleted);
        Assert.True((await db.Folders.FindAsync(f2))!.IsDeleted);
        Assert.True((await db.Folders.FindAsync(f3))!.IsDeleted);
        Assert.True((await db.Notes.FindAsync(n1))!.IsDeleted);
        Assert.True((await db.Notes.FindAsync(n2))!.IsDeleted);
        Assert.True((await db.Notes.FindAsync(n3))!.IsDeleted);

        var deleteLogs = await db.SyncLogs
            .Where(s => s.UserId == userId && s.Action == ChangeActions.Delete)
            .ToListAsync();

        Assert.Equal(6, deleteLogs.Count);
        Assert.Equal(new long[] { 11, 12, 13, 14, 15, 16 },
            deleteLogs.Select(s => s.Version).OrderBy(v => v).ToArray());
    }
}

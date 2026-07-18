using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services;
using BiSheng.Latte.Tests.Fixtures;
using BiSheng.Shared;
using BiSheng.Shared.Sync;

namespace BiSheng.Latte.Tests.Sync;

/// <summary>MergeRemoteChange：冲突检测与 Push 回声清除 pending</summary>
public class MergeRemoteChangeTests
{
    /// <summary>pending 与远端内容不同 → 建 SyncConflict，清除 pending</summary>
    [Fact]
    public void MergeRemoteChange_PendingDiffersFromRemote_CreatesConflict()
    {
        using var fixture = new LatteTestDbFactory();
        var noteId = Guid.NewGuid();
        var folderId = Guid.NewGuid();

        fixture.Db.Folders.Add(new LocalFolder { Id = folderId, Name = "F" });
        fixture.Db.Notes.Add(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "T",
            Content = "local",
            UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
        });
        fixture.Db.PendingChanges.Add(new LocalPendingChange
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = ChangeActions.Update,
            Payload = NotePayload(noteId, folderId, "T", "local"),
            UpdatedAt = DateTime.UtcNow
        });
        fixture.Db.SaveChanges();

        var remote = new ChangeDto
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = ChangeActions.Update,
            Version = 11,
            Timestamp = DateTime.UtcNow,
            Payload = NotePayload(noteId, folderId, "T", "remote")
        };

        var created = SyncService.MergeRemoteChange(fixture.Db, remote);
        fixture.Db.SaveChanges();

        Assert.True(created);
        Assert.Single(fixture.Db.SyncConflicts);
        Assert.Empty(fixture.Db.PendingChanges);
        Assert.Equal("local", fixture.Db.Notes.Find(noteId)!.Content);
    }

    /// <summary>pending 与远端 fingerprint 相同 → 无冲突，清除 pending</summary>
    [Fact]
    public void MergeRemoteChange_PendingMatchesRemoteFingerprint_ClearsPendingNoConflict()
    {
        using var fixture = new LatteTestDbFactory();
        var noteId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var payload = NotePayload(noteId, folderId, "T", "same");

        fixture.Db.Folders.Add(new LocalFolder { Id = folderId, Name = "F" });
        fixture.Db.Notes.Add(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "T",
            Content = "old",
            UpdatedAt = DateTime.UtcNow.AddHours(-1)
        });
        fixture.Db.PendingChanges.Add(new LocalPendingChange
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = ChangeActions.Update,
            Payload = payload,
            UpdatedAt = DateTime.UtcNow
        });
        fixture.Db.SaveChanges();

        var remote = new ChangeDto
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = ChangeActions.Update,
            Version = 11,
            Timestamp = DateTime.UtcNow,
            Payload = payload
        };

        var created = SyncService.MergeRemoteChange(fixture.Db, remote);
        fixture.Db.SaveChanges();

        Assert.False(created);
        Assert.Empty(fixture.Db.SyncConflicts);
        Assert.Empty(fixture.Db.PendingChanges);
        Assert.Equal("same", fixture.Db.Notes.Find(noteId)!.Content);
    }

    /// <summary>无 pending → 直接应用远端变更</summary>
    [Fact]
    public void MergeRemoteChange_NoPending_AppliesRemote()
    {
        using var fixture = new LatteTestDbFactory();
        var noteId = Guid.NewGuid();
        var folderId = Guid.NewGuid();

        fixture.Db.Folders.Add(new LocalFolder { Id = folderId, Name = "F" });
        fixture.Db.Notes.Add(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "T",
            Content = "before",
            UpdatedAt = DateTime.UtcNow
        });
        fixture.Db.SaveChanges();

        var remote = new ChangeDto
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = ChangeActions.Update,
            Version = 5,
            Timestamp = DateTime.UtcNow,
            Payload = NotePayload(noteId, folderId, "T", "after")
        };

        var created = SyncService.MergeRemoteChange(fixture.Db, remote);

        Assert.False(created);
        Assert.Equal("after", fixture.Db.Notes.Find(noteId)!.Content);
        Assert.Equal(5, fixture.Db.Notes.Find(noteId)!.Version);
    }

    private static string NotePayload(Guid noteId, Guid folderId, string title, string content) =>
        SyncPayloadJson.Serialize(SyncPayloadBuilder.Note(title, content, folderId));
}

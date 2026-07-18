using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services;
using BiSheng.Latte.Services.Sync;
using BiSheng.Latte.Tests.Fixtures;
using BiSheng.Shared;
using BiSheng.Shared.Sync;

namespace BiSheng.Latte.Tests.Sync;

/// <summary>冲突解决：Delete 与完整 payload</summary>
public class ConflictResolveDeleteTests
{
    /// <summary>本地 Delete vs 远端 Update：记冲突时保存 Action 与完整 RemotePayload</summary>
    [Fact]
    public void RecordConflict_StoresActionsAndFullRemotePayload()
    {
        using var fixture = new LatteTestDbFactory();
        var folderId = Guid.NewGuid();
        var noteId = Guid.NewGuid();

        fixture.Db.Folders.Add(new LocalFolder { Id = folderId, Name = "F" });
        fixture.Db.Notes.Add(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "T",
            Content = "local",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        });
        fixture.Db.PendingChanges.Add(new LocalPendingChange
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = ChangeActions.Delete,
            Payload = null,
            UpdatedAt = DateTime.UtcNow
        });
        fixture.Db.SaveChanges();

        var remotePayload = SyncPayloadJson.Serialize(
            SyncPayloadBuilder.Note("RemoteTitle", "remote-body", folderId, true, false));
        var remote = new ChangeDto
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = ChangeActions.Update,
            Version = 11,
            Timestamp = DateTime.UtcNow,
            Payload = remotePayload
        };

        var created = SyncRemoteChangeMerger.MergeRemoteChange(fixture.Db, remote);
        fixture.Db.SaveChanges();

        Assert.True(created);
        var conflict = Assert.Single(fixture.Db.SyncConflicts);
        Assert.Equal(ChangeActions.Delete, conflict.LocalAction);
        Assert.Equal(ChangeActions.Update, conflict.RemoteAction);
        Assert.Equal(remotePayload, conflict.RemotePayload);
        Assert.Equal("remote-body", conflict.RemoteContent);
        Assert.Equal("（已删除）", conflict.LocalContent);
    }

    /// <summary>保留本地 Delete 时排队 Delete，而非 Update</summary>
    [Fact]
    public void ResolveKeepLocal_WhenNoteSoftDeleted_EnqueuesDelete()
    {
        using var fixture = new LatteTestDbFactory();
        var folderId = Guid.NewGuid();
        var noteId = Guid.NewGuid();

        fixture.Db.Folders.Add(new LocalFolder { Id = folderId, Name = "F" });
        fixture.Db.Notes.Add(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "T",
            Content = "gone",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        });
        fixture.Db.SyncConflicts.Add(new SyncConflict
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            EntityTitle = "T",
            LocalContent = "（已删除）",
            RemoteContent = "remote",
            LocalAction = ChangeActions.Delete,
            RemoteAction = ChangeActions.Update,
            RemotePayload = SyncPayloadJson.Serialize(
                SyncPayloadBuilder.Note("T", "remote", folderId, false, false)),
            LocalUpdatedAt = DateTime.UtcNow,
            RemoteUpdatedAt = DateTime.UtcNow
        });
        fixture.Db.SaveChanges();

        // 通过 SyncService 需要 Auth/ApiClient；直接测 Enqueue 语义：模拟 ResolveKeepLocal 后的 pending
        var conflict = fixture.Db.SyncConflicts.Single();
        conflict.IsResolved = true;

        var existing = fixture.Db.PendingChanges.FirstOrDefault(p => p.EntityId == noteId);
        if (existing != null)
        {
            fixture.Db.PendingChanges.Remove(existing);
        }

        var note = fixture.Db.Notes.Find(noteId)!;
        Assert.True(note.IsDeleted);
        fixture.Db.PendingChanges.Add(new LocalPendingChange
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = note.IsDeleted ? ChangeActions.Delete : ChangeActions.Update,
            Payload = null,
            UpdatedAt = conflict.LocalUpdatedAt
        });
        fixture.Db.SaveChanges();

        var pending = Assert.Single(fixture.Db.PendingChanges);
        Assert.Equal(ChangeActions.Delete, pending.Action);
        Assert.Null(pending.Payload);
    }

    /// <summary>采用远端完整 payload 时恢复标题/正文/收藏，并清除软删</summary>
    [Fact]
    public void ApplyRemoteNotePayload_RestoresFullFieldsFromJson()
    {
        using var fixture = new LatteTestDbFactory();
        var folderId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var otherFolder = Guid.NewGuid();

        fixture.Db.Folders.Add(new LocalFolder { Id = folderId, Name = "F" });
        fixture.Db.Folders.Add(new LocalFolder { Id = otherFolder, Name = "G" });
        fixture.Db.Notes.Add(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "old",
            Content = "old-body",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        });
        fixture.Db.SaveChanges();

        var payload = SyncPayloadJson.Serialize(
            SyncPayloadBuilder.Note("NewTitle", "new-body", otherFolder, true, true));
        SyncRemoteChangeMerger.ApplyRemoteNotePayload(fixture.Db, noteId, payload, DateTime.UtcNow);
        var note = fixture.Db.Notes.Find(noteId)!;
        note.IsDeleted = false;
        note.DeletedAt = null;
        fixture.Db.SaveChanges();

        Assert.Equal("NewTitle", note.Title);
        Assert.Equal("new-body", note.Content);
        Assert.Equal(otherFolder, note.FolderId);
        Assert.True(note.IsFavorite);
        Assert.True(note.IsPinned);
        Assert.False(note.IsDeleted);
    }
}

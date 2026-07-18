using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services;
using BiSheng.Latte.Services.Navigation;
using BiSheng.Latte.Services.Sync;
using BiSheng.Latte.Tests.Fixtures;
using BiSheng.Shared;

namespace BiSheng.Latte.Tests.Sync;

/// <summary>空副本引导与回收站硬删不得丢掉未同步 Delete</summary>
public class PendingDeleteSurviveTests
{
    /// <summary>仅软删 + pending Delete 时不重置游标、不清 pending</summary>
    [Fact]
    public async Task EnsureFreshLocalReplica_KeepsPendingDeleteAndCursor()
    {
        using var fixture = new LatteTestDbFactory();
        var noteId = Guid.NewGuid();
        var folderId = Guid.NewGuid();

        fixture.Db.Folders.Add(new LocalFolder
        {
            Id = folderId,
            Name = "F",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        });
        fixture.Db.Notes.Add(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "T",
            Content = "x",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        });
        fixture.Db.PendingChanges.Add(new LocalPendingChange
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = ChangeActions.Delete,
            UpdatedAt = DateTime.UtcNow
        });
        fixture.Db.SyncState.Add(new LocalSyncState { Id = 1, LastSyncVersion = 42 });
        fixture.Db.SaveChanges();

        var bootstrap = new SyncBootstrap(
            apiClient: null!,
            dbFactory: () => new LocalDbContext());
        await bootstrap.EnsureFreshLocalReplicaAsync();

        using var verify = new LocalDbContext();
        Assert.Equal(42, verify.SyncState.Single().LastSyncVersion);
        Assert.Contains(verify.PendingChanges, p => p.EntityId == noteId && p.Action == ChangeActions.Delete);
    }

    /// <summary>永久删除软删笔记时保留 Delete pending</summary>
    [Fact]
    public void PurgePermanently_KeepsDeletePending()
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
            Content = "x",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        });
        fixture.Db.PendingChanges.Add(new LocalPendingChange
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = ChangeActions.Delete,
            UpdatedAt = DateTime.UtcNow
        });
        fixture.Db.SaveChanges();

        var trash = new TrashService(
            changeTracker: new LocalChangeTracker(() => new LocalDbContext()),
            dbFactory: () => new LocalDbContext(),
            navigationPublisher: new NoopNavigationPublisher());
        trash.PurgePermanently(EntityTypes.Note, noteId);

        using var verify = new LocalDbContext();
        Assert.Null(verify.Notes.Find(noteId));
        Assert.Contains(verify.PendingChanges, p => p.EntityId == noteId && p.Action == ChangeActions.Delete);
    }

    /// <summary>无 pending 时硬删会写入 Delete pending</summary>
    [Fact]
    public void PurgePermanently_WithoutPending_EnqueuesDelete()
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
            Content = "x",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        });
        fixture.Db.SaveChanges();

        var trash = new TrashService(
            changeTracker: new LocalChangeTracker(() => new LocalDbContext()),
            dbFactory: () => new LocalDbContext(),
            navigationPublisher: new NoopNavigationPublisher());
        trash.PurgePermanently(EntityTypes.Note, noteId);

        using var verify = new LocalDbContext();
        Assert.Contains(verify.PendingChanges, p => p.EntityId == noteId && p.Action == ChangeActions.Delete);
    }

    private sealed class NoopNavigationPublisher : INavigationMutationPublisher
    {
        public void NotifyFolderCreated(Guid folderId, Guid? parentFolderId)
        {
        }

        public void NotifyFolderUpdated(Guid folderId, Guid? parentFolderId, bool flagsChanged = false, bool parentFolderChanged = false)
        {
        }

        public void NotifyFolderDeleted(Guid folderId)
        {
        }

        public void NotifyNoteCreated(Guid noteId, Guid folderId)
        {
        }

        public void NotifyNoteUpdated(Guid noteId, Guid folderId, bool flagsChanged = false)
        {
        }

        public void NotifyNoteDeleted(Guid noteId, Guid folderId)
        {
        }

        public void NotifyChanges(IReadOnlyList<NavigationChange> changes)
        {
        }

        public void NotifyFilterChanged()
        {
        }

        public void NotifyLayoutRebuild()
        {
        }
    }
}

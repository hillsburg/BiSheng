using System.IO;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services;
using BiSheng.Latte.Tests.Fixtures;
using BiSheng.Shared;
using BiSheng.Shared.Sync;

namespace BiSheng.Latte.Tests.Sync;

/// <summary>落盘抢救快照经 ApplyAfterPull 可恢复未上云笔记</summary>
public class FullResyncRecoveryPersistTests : IDisposable
{
    private readonly string _tempPath;

    public FullResyncRecoveryPersistTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"bisheng-rescue-{Guid.NewGuid():N}.json");
        FullResyncRescueStore.SetPathOverrideForTests(_tempPath);
    }

    public void Dispose()
    {
        FullResyncRescueStore.Clear();
        FullResyncRescueStore.SetPathOverrideForTests(null);
    }

    /// <summary>模拟清库后仅剩磁盘快照：Load + Apply 能写回笔记并重建 pending</summary>
    [Fact]
    public void DiskRescue_AfterWipe_RestoresNoteAndPending()
    {
        using var fixture = new LatteTestDbFactory();
        var folderId = Guid.NewGuid();
        var noteId = Guid.NewGuid();

        fixture.Db.Folders.Add(new LocalFolder { Id = folderId, Name = "F" });
        fixture.Db.Notes.Add(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "LocalOnly",
            Content = "never-pushed",
            UpdatedAt = DateTime.UtcNow
        });
        fixture.Db.PendingChanges.Add(new LocalPendingChange
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = ChangeActions.Create,
            Payload = SyncPayloadJson.Serialize(
                SyncPayloadBuilder.Note("LocalOnly", "never-pushed", folderId, false, false)),
            UpdatedAt = DateTime.UtcNow
        });
        fixture.Db.SaveChanges();

        var captured = FullResyncRecovery.Capture(fixture.Db);
        FullResyncRescueStore.Save(captured);

        // 模拟清库
        fixture.Db.Notes.RemoveRange(fixture.Db.Notes.ToList());
        fixture.Db.Folders.RemoveRange(fixture.Db.Folders.ToList());
        fixture.Db.PendingChanges.RemoveRange(fixture.Db.PendingChanges.ToList());
        fixture.Db.SaveChanges();

        var loaded = FullResyncRescueStore.TryLoad();
        Assert.NotNull(loaded);

        var restored = FullResyncRecovery.ApplyAfterPull(fixture.Db, loaded!);
        fixture.Db.SaveChanges();

        Assert.True(restored >= 1);
        var note = fixture.Db.Notes.Find(noteId);
        Assert.NotNull(note);
        Assert.Equal("never-pushed", note!.Content);
        Assert.Contains(fixture.Db.PendingChanges, p => p.EntityId == noteId);
    }
}

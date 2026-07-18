using System.IO;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services;
using BiSheng.Shared;

namespace BiSheng.Latte.Tests.Sync;

/// <summary>全量重建抢救快照落盘与回读</summary>
public class FullResyncRescueStoreTests : IDisposable
{
    private readonly string _tempPath;

    public FullResyncRescueStoreTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"bisheng-rescue-{Guid.NewGuid():N}.json");
        FullResyncRescueStore.SetPathOverrideForTests(_tempPath);
    }

    public void Dispose()
    {
        FullResyncRescueStore.Clear();
        FullResyncRescueStore.SetPathOverrideForTests(null);
    }

    /// <summary>Save 后 Exists，TryLoad 能还原 Pending 与实体字段</summary>
    [Fact]
    public void SaveAndLoad_RoundTripsPendingAndEntitySnapshot()
    {
        var noteId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var snapshot = new FullResyncRecovery.RescueSnapshot
        {
            CapturedAtUtc = DateTime.UtcNow,
            Entries =
            {
                new FullResyncRecovery.RescuedEntry
                {
                    EntityType = EntityTypes.Note,
                    EntityId = noteId,
                    Action = ChangeActions.Update,
                    Payload = "{\"title\":\"T\",\"content\":\"unsynced\"}",
                    UpdatedAt = DateTime.UtcNow,
                    Note = new LocalNote
                    {
                        Id = noteId,
                        Title = "T",
                        Content = "unsynced",
                        FolderId = folderId,
                        IsFavorite = true,
                        UpdatedAt = DateTime.UtcNow
                    }
                }
            }
        };

        FullResyncRescueStore.Save(snapshot);

        Assert.True(FullResyncRescueStore.Exists());
        var loaded = FullResyncRescueStore.TryLoad();
        Assert.NotNull(loaded);
        Assert.Single(loaded!.Entries);
        Assert.Equal(noteId, loaded.Entries[0].EntityId);
        Assert.Equal("unsynced", loaded.Entries[0].Note?.Content);
        Assert.True(loaded.Entries[0].Note?.IsFavorite);
    }

    /// <summary>Clear 后文件消失</summary>
    [Fact]
    public void Clear_RemovesRescueFile()
    {
        FullResyncRescueStore.Save(new FullResyncRecovery.RescueSnapshot
        {
            CapturedAtUtc = DateTime.UtcNow
        });
        Assert.True(FullResyncRescueStore.Exists());

        FullResyncRescueStore.Clear();
        Assert.False(FullResyncRescueStore.Exists());
        Assert.Null(FullResyncRescueStore.TryLoad());
    }
}

using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services;
using BiSheng.Latte.Tests.Fixtures;
using BiSheng.Shared;

namespace BiSheng.Latte.Tests.Sync;

/// <summary>已解决冲突记录按保留期裁剪</summary>
public class SyncConflictCleanupTests
{
    [Fact]
    public void Prune_RemovesOnlyResolvedOlderThanCutoff()
    {
        using var fixture = new LatteTestDbFactory();
        var cutoff = DateTime.UtcNow.AddDays(-SyncConflictCleanupService.ResolvedRetentionDays);

        fixture.Db.SyncConflicts.AddRange(
            new SyncConflict
            {
                EntityType = EntityTypes.Note,
                EntityId = Guid.NewGuid(),
                EntityTitle = "old-resolved",
                LocalContent = "a",
                RemoteContent = "b",
                IsResolved = true,
                CreatedAt = cutoff.AddDays(-1)
            },
            new SyncConflict
            {
                EntityType = EntityTypes.Note,
                EntityId = Guid.NewGuid(),
                EntityTitle = "recent-resolved",
                LocalContent = "a",
                RemoteContent = "b",
                IsResolved = true,
                CreatedAt = cutoff.AddDays(1)
            },
            new SyncConflict
            {
                EntityType = EntityTypes.Note,
                EntityId = Guid.NewGuid(),
                EntityTitle = "old-unresolved",
                LocalContent = "a",
                RemoteContent = "b",
                IsResolved = false,
                CreatedAt = cutoff.AddDays(-1)
            });
        fixture.Db.SaveChanges();

        var removed = SyncConflictCleanupService.Prune(fixture.Db, cutoff);

        Assert.Equal(1, removed);
        Assert.Equal(2, fixture.Db.SyncConflicts.Count());
        Assert.DoesNotContain(fixture.Db.SyncConflicts, c => c.EntityTitle == "old-resolved");
        Assert.Contains(fixture.Db.SyncConflicts, c => c.EntityTitle == "recent-resolved");
        Assert.Contains(fixture.Db.SyncConflicts, c => c.EntityTitle == "old-unresolved");
    }
}

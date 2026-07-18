using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services;
using BiSheng.Latte.Tests.Fixtures;
using BiSheng.Shared;
using BiSheng.Shared.Sync;

namespace BiSheng.Latte.Tests.Sync;

/// <summary>OrderPendingForPush：文件夹拓扑排序与环检测</summary>
public class OrderPendingForPushTests
{
    /// <summary>A.parent=B 且 B.parent=A → 两者均记入 cyclicIds，不参与排序结果</summary>
    [Fact]
    public void OrderPendingForPush_FolderCycle_ReportsCyclicIds()
    {
        var folderA = Guid.NewGuid();
        var folderB = Guid.NewGuid();

        var pending = new List<LocalPendingChange>
        {
            new()
            {
                EntityType = EntityTypes.Folder,
                EntityId = folderA,
                Action = ChangeActions.Create,
                Payload = FolderPayload(folderA, folderB, "A")
            },
            new()
            {
                EntityType = EntityTypes.Folder,
                EntityId = folderB,
                Action = ChangeActions.Create,
                Payload = FolderPayload(folderB, folderA, "B")
            }
        };

        var (sorted, cyclicIds) = SyncService.OrderPendingForPush(pending);

        Assert.Contains(folderA, cyclicIds);
        Assert.Contains(folderB, cyclicIds);
        Assert.DoesNotContain(folderA, sorted.Select(p => p.EntityId));
    }

    /// <summary>父先于子：parent Create 排在 child Create 之前</summary>
    [Fact]
    public void OrderPendingForPush_ParentBeforeChild_SortsCorrectly()
    {
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var pending = new List<LocalPendingChange>
        {
            new()
            {
                EntityType = EntityTypes.Folder,
                EntityId = childId,
                Action = ChangeActions.Create,
                Payload = FolderPayload(childId, parentId, "Child")
            },
            new()
            {
                EntityType = EntityTypes.Folder,
                EntityId = parentId,
                Action = ChangeActions.Create,
                Payload = FolderPayload(parentId, null, "Parent")
            },
            new()
            {
                EntityType = EntityTypes.Note,
                EntityId = Guid.NewGuid(),
                Action = ChangeActions.Create,
                Payload = "{}"
            }
        };

        var (sorted, cyclicIds) = SyncService.OrderPendingForPush(pending);

        Assert.Empty(cyclicIds);
        var folderOrder = sorted
            .Where(p => p.EntityType == EntityTypes.Folder)
            .Select(p => p.EntityId)
            .ToList();
        Assert.Equal(new[] { parentId, childId }, folderOrder);
        Assert.Equal(EntityTypes.Note, sorted[^1].EntityType);
    }

    private static string FolderPayload(Guid id, Guid? parentId, string name) =>
        SyncPayloadJson.Serialize(new { name, parentId, isFavorite = false, isPinned = false });
}

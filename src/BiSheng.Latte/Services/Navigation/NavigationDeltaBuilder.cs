using BiSheng.Latte.Data;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using System.Text.Json;

namespace BiSheng.Latte.Services.Navigation;

/// <summary>从 ChangeDto 构建 NavigationChange（在 Apply 之前调用，以便检测 Parent 变化）</summary>
public static class NavigationDeltaBuilder
{
    /// <summary>单条 ChangeDto → NavigationChange</summary>
    public static NavigationChange FromChangeDto(Func<LocalDbContext> dbFactory, ChangeDto change)
    {
        Guid? folderId = null;
        Guid? parentFolderId = null;
        var flagsChanged = false;
        var parentFolderChanged = false;

        if (change.Payload != null)
        {
            using var doc = JsonDocument.Parse(change.Payload);
            var root = doc.RootElement;

            if (change.EntityType == EntityTypes.Note)
            {
                folderId = SyncPayloadReader.ReadNullableGuid(root, "folderId");
                if (change.Action != ChangeActions.Delete)
                {
                    flagsChanged = root.TryGetProperty("isFavorite", out _)
                        || root.TryGetProperty("isPinned", out _)
                        || SyncPayloadReader.TryGetPropertyIgnoreCase(root, "isFavorite", out _)
                        || SyncPayloadReader.TryGetPropertyIgnoreCase(root, "isPinned", out _);
                }
            }
            else if (change.EntityType == EntityTypes.Folder)
            {
                parentFolderId = SyncPayloadReader.ReadNullableGuid(root, "parentId");
                if (change.Action == ChangeActions.Update && parentFolderId.HasValue)
                {
                    using var db = dbFactory();
                    var existing = db.Folders.Find(change.EntityId);
                    if (existing != null && existing.ParentId != parentFolderId)
                    {
                        parentFolderChanged = true;
                    }
                }

                if (change.Action != ChangeActions.Delete)
                {
                    flagsChanged = SyncPayloadReader.TryGetPropertyIgnoreCase(root, "isFavorite", out _)
                        || SyncPayloadReader.TryGetPropertyIgnoreCase(root, "isPinned", out _);
                }
            }
        }

        if (change.EntityType == EntityTypes.Note && !folderId.HasValue && change.Action != ChangeActions.Delete)
        {
            using var db = dbFactory();
            folderId = db.Notes.Find(change.EntityId)?.FolderId;
        }

        if (change.EntityType == EntityTypes.Folder && !parentFolderId.HasValue && change.Action != ChangeActions.Delete)
        {
            using var db = dbFactory();
            parentFolderId = db.Folders.Find(change.EntityId)?.ParentId;
        }

        return new NavigationChange
        {
            EntityType = change.EntityType,
            EntityId = change.EntityId,
            Action = change.Action,
            FolderId = folderId,
            ParentFolderId = parentFolderId,
            FlagsChanged = flagsChanged,
            ParentFolderChanged = parentFolderChanged
        };
    }

    /// <summary>批量构建</summary>
    public static List<NavigationChange> FromChangeDtos(
        Func<LocalDbContext> dbFactory,
        IEnumerable<ChangeDto> changes)
    {
        return changes.Select(c => FromChangeDto(dbFactory, c)).ToList();
    }
}

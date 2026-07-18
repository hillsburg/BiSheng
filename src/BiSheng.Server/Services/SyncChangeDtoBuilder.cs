using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Services;

/// <summary>
/// 从实体表或 SyncLog 构建 Pull / Notify 用的 ChangeDto
/// </summary>
internal static class SyncChangeDtoBuilder
{
    /// <summary>
    /// 终态折叠：根据实体类型和 ID 从实体表查询最新状态，构建 ChangeDto
    /// </summary>
    public static async Task<ChangeDto?> BuildFromEntityAsync(
        AppDbContext db,
        Guid userId,
        string entityType,
        Guid entityId,
        SyncLog latestLog,
        long batchMaxVersion,
        CancellationToken ct)
    {
        if (entityType == EntityTypes.Folder)
        {
            var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == entityId, ct);
            if (folder == null)
            {
                // 实体不在表中（防御性处理）：回退到 SyncLog 原始 Payload
                return new ChangeDto
                {
                    EntityType = entityType,
                    EntityId = entityId,
                    Action = latestLog.Action,
                    Version = batchMaxVersion,
                    Timestamp = latestLog.Timestamp,
                    Payload = latestLog.Payload
                };
            }

            if (folder.IsDeleted)
            {
                return new ChangeDto
                {
                    EntityType = entityType,
                    EntityId = entityId,
                    Action = ChangeActions.Delete,
                    Version = batchMaxVersion,
                    Timestamp = folder.UpdatedAt
                };
            }

            return new ChangeDto
            {
                EntityType = entityType,
                EntityId = entityId,
                Action = ChangeActions.Update,
                Version = batchMaxVersion,
                Timestamp = folder.UpdatedAt,
                Payload = SyncPayloadJson.Serialize(SyncPayloadBuilder.FolderPayload(
                    folder.Name, folder.ParentId, folder.IsFavorite, folder.IsPinned))
            };
        }

        if (entityType == EntityTypes.Note)
        {
            var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == entityId, ct);
            if (note == null)
            {
                return new ChangeDto
                {
                    EntityType = entityType,
                    EntityId = entityId,
                    Action = latestLog.Action,
                    Version = batchMaxVersion,
                    Timestamp = latestLog.Timestamp,
                    Payload = latestLog.Payload
                };
            }

            if (note.IsDeleted)
            {
                return new ChangeDto
                {
                    EntityType = entityType,
                    EntityId = entityId,
                    Action = ChangeActions.Delete,
                    Version = batchMaxVersion,
                    Timestamp = note.UpdatedAt
                };
            }

            return new ChangeDto
            {
                EntityType = entityType,
                EntityId = entityId,
                Action = ChangeActions.Update,
                Version = batchMaxVersion,
                Timestamp = note.UpdatedAt,
                Payload = SyncPayloadJson.Serialize(SyncPayloadBuilder.NotePayload(
                    note.Title, note.Content, note.FolderId, note.IsFavorite, note.IsPinned))
            };
        }

        return null;
    }

    /// <summary>
    /// 实体快照：按当前 Folder/Note 行构建 ChangeDto（Version 取实体版本）
    /// </summary>
    public static ChangeDto? BuildFromLiveFolder(Folder folder)
    {
        if (folder.IsDeleted)
        {
            return new ChangeDto
            {
                EntityType = EntityTypes.Folder,
                EntityId = folder.Id,
                Action = ChangeActions.Delete,
                Version = folder.Version,
                Timestamp = folder.UpdatedAt
            };
        }

        return new ChangeDto
        {
            EntityType = EntityTypes.Folder,
            EntityId = folder.Id,
            Action = ChangeActions.Update,
            Version = folder.Version,
            Timestamp = folder.UpdatedAt,
            Payload = SyncPayloadJson.Serialize(SyncPayloadBuilder.FolderPayload(
                folder.Name, folder.ParentId, folder.IsFavorite, folder.IsPinned))
        };
    }

    /// <summary>
    /// 实体快照：按当前 Note 行构建 ChangeDto（Version 取实体版本）
    /// </summary>
    public static ChangeDto? BuildFromLiveNote(Note note)
    {
        if (note.IsDeleted)
        {
            return new ChangeDto
            {
                EntityType = EntityTypes.Note,
                EntityId = note.Id,
                Action = ChangeActions.Delete,
                Version = note.Version,
                Timestamp = note.UpdatedAt
            };
        }

        return new ChangeDto
        {
            EntityType = EntityTypes.Note,
            EntityId = note.Id,
            Action = ChangeActions.Update,
            Version = note.Version,
            Timestamp = note.UpdatedAt,
            Payload = SyncPayloadJson.Serialize(SyncPayloadBuilder.NotePayload(
                note.Title, note.Content, note.FolderId, note.IsFavorite, note.IsPinned))
        };
    }
}

using System.Text.Json;
using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Services.Mutations;

/// <summary>
/// 实体变更 Writer 实现：从 SyncService.ApplyClientChange 抽出，供 REST / Push 共用（PR-1）。
/// </summary>
public sealed class EntityChangeWriter : IEntityChangeWriter
{
    private readonly UserSyncVersionService _versionService;
    private readonly NoteRevisionService _noteRevisions;

    /// <summary>构造 Writer</summary>
    public EntityChangeWriter(
        UserSyncVersionService versionService,
        NoteRevisionService noteRevisions)
    {
        _versionService = versionService;
        _noteRevisions = noteRevisions;
    }

    /// <inheritdoc />
    public async Task<MutationApplyResult> TryApplyAsync(
        AppDbContext db,
        Guid userId,
        EntityMutation mutation,
        MutationBatchContext? batchContext,
        MutationWriteOptions options,
        CancellationToken ct = default)
    {
        var availableFolderIds = batchContext?.AvailableFolderIds;
        var applied = false;
        Note? affectedNote = null;
        IReadOnlyList<FolderCascadeDeleter.CascadeEntry> cascaded =
            Array.Empty<FolderCascadeDeleter.CascadeEntry>();

        if (mutation.EntityType == EntityTypes.Folder)
        {
            var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == mutation.EntityId, ct);
            if (folder == null && mutation.Action != ChangeActions.Delete)
            {
                var payload = JsonDocument.Parse(mutation.Payload ?? "{}");
                var root = payload.RootElement;
                var parentId = SyncPayloadReader.ReadNullableGuid(root, "parentId");
                if (!await FolderGraphValidator.IsValidParentAsync(
                        db, userId, mutation.EntityId, parentId, availableFolderIds, ct))
                {
                    return new MutationSkipped("父文件夹无效或尚未创建");
                }

                folder = new Folder
                {
                    Id = mutation.EntityId,
                    Name = SyncPayloadReader.ReadString(root, "name"),
                    ParentId = parentId,
                    IsFavorite = SyncPayloadReader.ReadBool(root, "isFavorite"),
                    IsPinned = SyncPayloadReader.ReadBool(root, "isPinned"),
                    UserId = userId,
                    UpdatedAt = mutation.UpdatedAt ?? DateTime.UtcNow
                };
                db.Folders.Add(folder);
                applied = true;
            }
            else if (folder != null && folder.UserId == userId)
            {
                if (mutation.Action == ChangeActions.Update || mutation.Action == ChangeActions.Create)
                {
                    var payload = JsonDocument.Parse(mutation.Payload ?? "{}");
                    var root = payload.RootElement;
                    var parentId = SyncPayloadReader.ReadNullableGuid(root, "parentId", folder.ParentId);
                    if (!await FolderGraphValidator.IsValidParentAsync(
                            db, userId, mutation.EntityId, parentId, availableFolderIds, ct))
                    {
                        return new MutationSkipped("父文件夹无效或尚未创建");
                    }

                    folder.Name = SyncPayloadReader.ReadString(root, "name", folder.Name);
                    folder.ParentId = parentId;
                    folder.IsFavorite = SyncPayloadReader.ReadBool(root, "isFavorite", folder.IsFavorite);
                    folder.IsPinned = SyncPayloadReader.ReadBool(root, "isPinned", folder.IsPinned);
                    folder.IsDeleted = false;
                    folder.UpdatedAt = mutation.UpdatedAt ?? DateTime.UtcNow;
                    applied = true;
                }
                else if (mutation.Action == ChangeActions.Delete)
                {
                    folder.IsDeleted = true;
                    folder.UpdatedAt = DateTime.UtcNow;
                    applied = true;

                    cascaded = await FolderCascadeDeleter.CascadeDeleteDescendantsAsync(
                        db, _versionService, userId, mutation.EntityId, ct);
                }
            }
        }
        else if (mutation.EntityType == EntityTypes.Note)
        {
            var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == mutation.EntityId, ct);
            if (note == null && mutation.Action != ChangeActions.Delete)
            {
                var payload = JsonDocument.Parse(mutation.Payload ?? "{}");
                var root = payload.RootElement;
                var folderId = SyncPayloadReader.ReadGuid(root, "folderId");
                if (folderId == Guid.Empty)
                {
                    return new MutationSkipped("笔记缺少有效 folderId");
                }

                if (!await FolderGraphValidator.FolderExistsAsync(
                        db, userId, folderId, availableFolderIds, ct))
                {
                    return new MutationSkipped("目标文件夹不存在");
                }

                note = new Note
                {
                    Id = mutation.EntityId,
                    Title = SyncPayloadReader.ReadString(root, "title"),
                    Content = SyncPayloadReader.ReadString(root, "content"),
                    FolderId = folderId,
                    IsFavorite = SyncPayloadReader.ReadBool(root, "isFavorite"),
                    IsPinned = SyncPayloadReader.ReadBool(root, "isPinned"),
                    UserId = userId,
                    UpdatedAt = mutation.UpdatedAt ?? DateTime.UtcNow
                };
                db.Notes.Add(note);
                affectedNote = note;
                applied = true;
            }
            else if (note != null && note.UserId == userId)
            {
                if (mutation.Action == ChangeActions.Update || mutation.Action == ChangeActions.Create)
                {
                    var payload = JsonDocument.Parse(mutation.Payload ?? "{}");
                    var root = payload.RootElement;
                    var folderId = SyncPayloadReader.ReadGuid(root, "folderId", note.FolderId);
                    if (!await FolderGraphValidator.FolderExistsAsync(
                            db, userId, folderId, availableFolderIds, ct))
                    {
                        return new MutationSkipped("目标文件夹不存在");
                    }

                    note.Title = SyncPayloadReader.ReadString(root, "title", note.Title);
                    note.Content = SyncPayloadReader.ReadString(root, "content", note.Content);
                    note.FolderId = folderId;
                    note.IsFavorite = SyncPayloadReader.ReadBool(root, "isFavorite", note.IsFavorite);
                    note.IsPinned = SyncPayloadReader.ReadBool(root, "isPinned", note.IsPinned);
                    note.IsDeleted = false;
                    note.UpdatedAt = mutation.UpdatedAt ?? DateTime.UtcNow;
                    affectedNote = note;
                    applied = true;
                }
                else if (mutation.Action == ChangeActions.Delete)
                {
                    note.IsDeleted = true;
                    note.UpdatedAt = DateTime.UtcNow;
                    applied = true;
                }
            }
        }

        if (!applied)
        {
            return new MutationSkipped("实体不存在或校验失败");
        }

        // 校验通过后再分配版本，避免无效变更消耗版本号
        var assignedVersion = await _versionService.ReserveNextVersionAsync(db, userId, ct);

        // Create 分支实体尚未 SaveChanges，须从 ChangeTracker.Local 取引用
        if (mutation.EntityType == EntityTypes.Folder)
        {
            var folder = db.Folders.Local.First(f => f.Id == mutation.EntityId);
            folder.Version = assignedVersion;
        }
        else if (mutation.EntityType == EntityTypes.Note)
        {
            var note = db.Notes.Local.First(n => n.Id == mutation.EntityId);
            note.Version = assignedVersion;
            affectedNote = note;
        }

        db.SyncLogs.Add(new SyncLog
        {
            EntityType = mutation.EntityType,
            EntityId = mutation.EntityId,
            Action = mutation.Action,
            Version = assignedVersion,
            UserId = userId,
            Payload = mutation.Payload
        });

        if (affectedNote != null && options.RecordNoteRevision)
        {
            await _noteRevisions.RecordIfChangedAsync(
                db,
                affectedNote,
                assignedVersion,
                ct,
                force: options.ForceNoteRevision);
        }

        var cascadedApplied = cascaded
            .Select(c => new CascadeAppliedMutation(c.EntityType, c.EntityId, c.Version, c.UpdatedAt))
            .ToList();

        return new MutationApplied(new AppliedMutation
        {
            Mutation = mutation,
            Version = assignedVersion,
            Payload = mutation.Payload,
            Cascaded = cascadedApplied
        });
    }
}

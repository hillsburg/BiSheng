using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using BiSheng.Server.DTOs;
using BiSheng.Server.Services;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Services.Mutations;

/// <summary>笔记 REST 变更：开事务 → Writer → Save → Commit → Notify</summary>
public sealed class NoteMutationService : INoteMutationService
{
    private readonly AppDbContext _db;
    private readonly IEntityChangeWriter _writer;
    private readonly ISyncChangeNotifier _notifier;
    private readonly ILogger<NoteMutationService> _logger;

    /// <summary>构造笔记变更服务</summary>
    public NoteMutationService(
        AppDbContext db,
        IEntityChangeWriter writer,
        ISyncChangeNotifier notifier,
        ILogger<NoteMutationService> logger)
    {
        _db = db;
        _writer = writer;
        _notifier = notifier;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<NoteMutationResult> CreateAsync(
        Guid userId,
        CreateNoteRequest request,
        CancellationToken ct = default)
    {
        if (!await FolderGraphValidator.FolderBelongsToUserAsync(_db, userId, request.FolderId, ct))
        {
            return BadRequest("文件夹不存在");
        }

        var noteId = Guid.NewGuid();
        var updatedAt = DateTime.UtcNow;
        var payload = SyncPayloadJson.Serialize(
            SyncPayloadBuilder.Note(request.Title, request.Content, request.FolderId));

        var mutation = new EntityMutation
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = ChangeActions.Create,
            Payload = payload,
            UpdatedAt = updatedAt
        };

        return await ApplyMutationAsync(userId, mutation, noteId, ct);
    }

    /// <inheritdoc />
    public async Task<NoteMutationResult> UpdateAsync(
        Guid userId,
        Guid noteId,
        UpdateNoteRequest request,
        CancellationToken ct = default)
    {
        if (!await _db.Notes.AnyAsync(n => n.Id == noteId && n.UserId == userId, ct))
        {
            return NotFound();
        }

        if (!await FolderGraphValidator.FolderBelongsToUserAsync(_db, userId, request.FolderId, ct))
        {
            return BadRequest("文件夹不存在");
        }

        var payload = SyncPayloadJson.Serialize(
            SyncPayloadBuilder.Note(request.Title, request.Content, request.FolderId));

        var mutation = new EntityMutation
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = ChangeActions.Update,
            Payload = payload,
            UpdatedAt = DateTime.UtcNow
        };

        return await ApplyMutationAsync(userId, mutation, noteId, ct, includeNote: false);
    }

    /// <inheritdoc />
    public async Task<NoteMutationResult> DeleteAsync(
        Guid userId,
        Guid noteId,
        CancellationToken ct = default)
    {
        if (!await _db.Notes.AnyAsync(n => n.Id == noteId && n.UserId == userId, ct))
        {
            return NotFound();
        }

        var mutation = new EntityMutation
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = ChangeActions.Delete,
            UpdatedAt = DateTime.UtcNow
        };

        return await ApplyMutationAsync(userId, mutation, noteId, ct, includeNote: false);
    }

    /// <inheritdoc />
    public async Task<NoteMutationResult> RestoreFromRevisionAsync(
        Guid userId,
        Guid noteId,
        Guid revisionId,
        CancellationToken ct = default)
    {
        var revision = await _db.NoteRevisions.FirstOrDefaultAsync(
            r => r.Id == revisionId && r.NoteId == noteId && r.UserId == userId,
            ct);
        if (revision == null)
        {
            return NotFound();
        }

        var note = await _db.Notes.FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId, ct);
        if (note == null)
        {
            return NotFound();
        }

        if (note.IsDeleted)
        {
            return BadRequest("笔记已删除，无法恢复历史版本");
        }

        var payload = SyncPayloadJson.Serialize(
            SyncPayloadBuilder.Note(
                revision.Title,
                revision.Content,
                note.FolderId,
                note.IsFavorite,
                note.IsPinned));

        var mutation = new EntityMutation
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = ChangeActions.Update,
            Payload = payload,
            UpdatedAt = DateTime.UtcNow
        };

        return await ApplyMutationAsync(
            userId,
            mutation,
            noteId,
            ct,
            forceNoteRevision: true);
    }

    /// <summary>统一事务：Writer → SaveChanges → Commit → Notify</summary>
    private async Task<NoteMutationResult> ApplyMutationAsync(
        Guid userId,
        EntityMutation mutation,
        Guid noteId,
        CancellationToken ct,
        bool includeNote = true,
        bool forceNoteRevision = false)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await _writer.TryApplyAsync(
                _db,
                userId,
                mutation,
                batchContext: null,
                new MutationWriteOptions { ForceNoteRevision = forceNoteRevision },
                ct);

            if (result is MutationSkipped skipped)
            {
                await transaction.RollbackAsync(ct);
                return BadRequest(skipped.Reason);
            }

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            await _notifier.NotifyAppliedAsync(userId, ((MutationApplied)result).Applied, ct);

            if (!includeNote)
            {
                return Success();
            }

            var note = await _db.Notes.SingleAsync(n => n.Id == noteId, ct);
            return Success(ToDto(note));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "笔记变更事务失败 {EntityId}, UserId: {UserId}", noteId, userId);
            return InternalError();
        }
    }

    private static NoteDto ToDto(Note note) => new()
    {
        Id = note.Id,
        Title = note.Title,
        Content = note.Content,
        FolderId = note.FolderId,
        IsFavorite = note.IsFavorite,
        IsPinned = note.IsPinned,
        IsDeleted = note.IsDeleted,
        Version = note.Version,
        CreatedAt = note.CreatedAt,
        UpdatedAt = note.UpdatedAt
    };

    private static NoteMutationResult Success(NoteDto? note = null) => new()
    {
        Outcome = MutationOutcome.Success,
        Note = note
    };

    private static NoteMutationResult NotFound() => new()
    {
        Outcome = MutationOutcome.NotFound
    };

    private static NoteMutationResult BadRequest(string message) => new()
    {
        Outcome = MutationOutcome.BadRequest,
        ErrorMessage = message
    };

    private static NoteMutationResult InternalError() => new()
    {
        Outcome = MutationOutcome.InternalError,
        ErrorMessage = "事务提交失败"
    };
}

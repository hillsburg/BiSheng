using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using BiSheng.Server.DTOs;
using BiSheng.Server.Services;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Services.Mutations;

/// <summary>文件夹 REST 变更：开事务 → Writer → Save → Commit → Notify</summary>
public sealed class FolderMutationService : IFolderMutationService
{
    private readonly AppDbContext _db;
    private readonly IEntityChangeWriter _writer;
    private readonly ISyncChangeNotifier _notifier;
    private readonly ILogger<FolderMutationService> _logger;

    /// <summary>构造文件夹变更服务</summary>
    public FolderMutationService(
        AppDbContext db,
        IEntityChangeWriter writer,
        ISyncChangeNotifier notifier,
        ILogger<FolderMutationService> logger)
    {
        _db = db;
        _writer = writer;
        _notifier = notifier;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<FolderMutationResult> CreateAsync(
        Guid userId,
        CreateFolderRequest request,
        CancellationToken ct = default)
    {
        var folderId = Guid.NewGuid();
        if (!await FolderGraphValidator.IsValidParentAsync(_db, userId, folderId, request.ParentId, ct))
        {
            return BadRequest("父文件夹无效");
        }

        var payload = SyncPayloadJson.Serialize(
            SyncPayloadBuilder.Folder(request.Name, request.ParentId));

        var mutation = new EntityMutation
        {
            EntityType = EntityTypes.Folder,
            EntityId = folderId,
            Action = ChangeActions.Create,
            Payload = payload,
            UpdatedAt = DateTime.UtcNow
        };

        return await ApplyMutationAsync(userId, mutation, folderId, ct);
    }

    /// <inheritdoc />
    public async Task<FolderMutationResult> UpdateAsync(
        Guid userId,
        Guid folderId,
        UpdateFolderRequest request,
        CancellationToken ct = default)
    {
        if (!await _db.Folders.AnyAsync(f => f.Id == folderId && f.UserId == userId, ct))
        {
            return NotFound();
        }

        if (!await FolderGraphValidator.IsValidParentAsync(_db, userId, folderId, request.ParentId, ct))
        {
            return BadRequest("父文件夹无效");
        }

        var payload = SyncPayloadJson.Serialize(
            SyncPayloadBuilder.Folder(request.Name, request.ParentId));

        var mutation = new EntityMutation
        {
            EntityType = EntityTypes.Folder,
            EntityId = folderId,
            Action = ChangeActions.Update,
            Payload = payload,
            UpdatedAt = DateTime.UtcNow
        };

        return await ApplyMutationAsync(userId, mutation, folderId, ct, includeFolder: false);
    }

    /// <inheritdoc />
    public async Task<FolderMutationResult> DeleteAsync(
        Guid userId,
        Guid folderId,
        CancellationToken ct = default)
    {
        if (!await _db.Folders.AnyAsync(f => f.Id == folderId && f.UserId == userId, ct))
        {
            return NotFound();
        }

        var mutation = new EntityMutation
        {
            EntityType = EntityTypes.Folder,
            EntityId = folderId,
            Action = ChangeActions.Delete,
            UpdatedAt = DateTime.UtcNow
        };

        return await ApplyMutationAsync(userId, mutation, folderId, ct, includeFolder: false);
    }

    /// <summary>统一事务：Writer → SaveChanges → Commit → Notify</summary>
    private async Task<FolderMutationResult> ApplyMutationAsync(
        Guid userId,
        EntityMutation mutation,
        Guid folderId,
        CancellationToken ct,
        bool includeFolder = true)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await _writer.TryApplyAsync(
                _db,
                userId,
                mutation,
                batchContext: null,
                new MutationWriteOptions(),
                ct);

            if (result is MutationSkipped skipped)
            {
                await transaction.RollbackAsync(ct);
                return BadRequest(skipped.Reason);
            }

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            await _notifier.NotifyAppliedAsync(userId, ((MutationApplied)result).Applied, ct);

            if (!includeFolder)
            {
                return Success();
            }

            var folder = await _db.Folders.SingleAsync(f => f.Id == folderId, ct);
            return Success(ToDto(folder));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "文件夹变更事务失败 {EntityId}, UserId: {UserId}", folderId, userId);
            return InternalError();
        }
    }

    private static FolderDto ToDto(Folder folder) => new()
    {
        Id = folder.Id,
        Name = folder.Name,
        ParentId = folder.ParentId,
        IsFavorite = folder.IsFavorite,
        IsPinned = folder.IsPinned,
        IsDeleted = folder.IsDeleted,
        Version = folder.Version,
        CreatedAt = folder.CreatedAt,
        UpdatedAt = folder.UpdatedAt
    };

    private static FolderMutationResult Success(FolderDto? folder = null) => new()
    {
        Outcome = MutationOutcome.Success,
        Folder = folder
    };

    private static FolderMutationResult NotFound() => new()
    {
        Outcome = MutationOutcome.NotFound
    };

    private static FolderMutationResult BadRequest(string message) => new()
    {
        Outcome = MutationOutcome.BadRequest,
        ErrorMessage = message
    };

    private static FolderMutationResult InternalError() => new()
    {
        Outcome = MutationOutcome.InternalError,
        ErrorMessage = "事务提交失败"
    };
}

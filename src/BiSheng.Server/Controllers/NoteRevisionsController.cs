using BiSheng.Server.Api;
using BiSheng.Server.Auth;
using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using BiSheng.Server.DTOs;
using BiSheng.Server.Services.Mutations;
using BiSheng.Shared.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Controllers;

/// <summary>
/// 笔记历史版本 API：列表、详情、恢复、手动删除。
/// 与 SyncLog 独立；笔记软删后历史仍保留，需用户主动清空。
/// </summary>
[Authorize(AuthenticationSchemes = ApiKeyAuthHandler.SchemeName)]
[ApiController]
[Route("api/notes/{noteId:guid}/revisions")]
public class NoteRevisionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly INoteMutationService _noteMutations;

    /// <summary>构造历史版本控制器</summary>
    public NoteRevisionsController(AppDbContext db, INoteMutationService noteMutations)
    {
        _db = db;
        _noteMutations = noteMutations;
    }

    private Guid UserId => User.GetUserId();

    /// <summary>获取指定笔记的历史版本列表（不含正文）</summary>
    [HttpGet]
    public async Task<ActionResult<List<NoteRevisionListItemDto>>> ListRevisions(Guid noteId)
    {
        if (!await NoteAccessibleAsync(noteId))
        {
            return ApiProblemResults.NotFound("笔记不存在或无权访问", HttpContext);
        }

        var items = await _db.NoteRevisions
            .Where(r => r.NoteId == noteId && r.UserId == UserId)
            .OrderByDescending(r => r.RevisionNumber)
            .Select(r => new NoteRevisionListItemDto
            {
                Id = r.Id,
                NoteId = r.NoteId,
                RevisionNumber = r.RevisionNumber,
                Title = r.Title,
                ContentHash = r.ContentHash,
                NoteVersion = r.NoteVersion,
                CreatedAt = r.CreatedAt
            })
            .ToListAsync();

        return Ok(items);
    }

    /// <summary>获取单条历史版本详情（含正文）</summary>
    [HttpGet("{revisionId:guid}")]
    public async Task<ActionResult<NoteRevisionDto>> GetRevision(Guid noteId, Guid revisionId)
    {
        var revision = await FindRevisionAsync(noteId, revisionId);
        if (revision == null)
        {
            return ApiProblemResults.NotFound("历史版本不存在", HttpContext);
        }

        return Ok(ToDto(revision));
    }

    /// <summary>将历史版本写回当前笔记，并产生一条新的历史快照</summary>
    [HttpPost("{revisionId:guid}/restore")]
    public async Task<ActionResult<NoteDto>> RestoreRevision(Guid noteId, Guid revisionId)
    {
        var result = await _noteMutations.RestoreFromRevisionAsync(UserId, noteId, revisionId);
        return MapMutationResult(result, success: r => Ok(r.Note));
    }

    /// <summary>删除单条历史版本</summary>
    [HttpDelete("{revisionId:guid}")]
    public async Task<IActionResult> DeleteRevision(Guid noteId, Guid revisionId)
    {
        var revision = await FindRevisionAsync(noteId, revisionId);
        if (revision == null)
        {
            return ApiProblemResults.NotFound("历史版本不存在", HttpContext);
        }

        _db.NoteRevisions.Remove(revision);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>手动删除该笔记的全部历史版本（笔记软删后历史仍保留，直至调用此接口）</summary>
    [HttpDelete]
    public async Task<IActionResult> DeleteAllRevisions(Guid noteId)
    {
        if (!await NoteOwnedAsync(noteId))
        {
            return ApiProblemResults.NotFound("笔记不存在或无权访问", HttpContext);
        }

        var revisions = await _db.NoteRevisions
            .Where(r => r.NoteId == noteId && r.UserId == UserId)
            .ToListAsync();

        _db.NoteRevisions.RemoveRange(revisions);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>将 MutationResult 映射为 HTTP 响应</summary>
    private ActionResult MapMutationResult(
        NoteMutationResult result,
        Func<NoteMutationResult, ActionResult> success)
    {
        if (result.Outcome == MutationOutcome.Success)
        {
            return success(result);
        }

        return ApiProblemResults.FromMutation(result.Outcome, result.ErrorMessage, HttpContext);
    }

    /// <summary>笔记存在且属于当前用户（含已软删，便于查看历史）</summary>
    private async Task<bool> NoteAccessibleAsync(Guid noteId) =>
        await _db.Notes.AnyAsync(n => n.Id == noteId && n.UserId == UserId);

    private async Task<bool> NoteOwnedAsync(Guid noteId) => await NoteAccessibleAsync(noteId);

    private async Task<NoteRevision?> FindRevisionAsync(Guid noteId, Guid revisionId) =>
        await _db.NoteRevisions.FirstOrDefaultAsync(
            r => r.Id == revisionId && r.NoteId == noteId && r.UserId == UserId);

    private static NoteRevisionDto ToDto(NoteRevision r) => new()
    {
        Id = r.Id,
        NoteId = r.NoteId,
        RevisionNumber = r.RevisionNumber,
        Title = r.Title,
        Content = r.Content,
        ContentHash = r.ContentHash,
        NoteVersion = r.NoteVersion,
        CreatedAt = r.CreatedAt
    };
}

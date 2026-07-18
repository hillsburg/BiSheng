using BiSheng.Server.Api;
using BiSheng.Server.Auth;
using BiSheng.Server.Data;
using BiSheng.Server.DTOs;
using BiSheng.Server.Services.Mutations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Controllers;

[Authorize(AuthenticationSchemes = ApiKeyAuthHandler.SchemeName)]
[ApiController]
[Route("api/[controller]")]
public class NotesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly INoteMutationService _noteMutations;

    /// <summary>构造笔记控制器</summary>
    public NotesController(AppDbContext db, INoteMutationService noteMutations)
    {
        _db = db;
        _noteMutations = noteMutations;
    }

    private Guid UserId => User.GetUserId();

    [HttpGet]
    public async Task<ActionResult<List<NoteListItemDto>>> GetNotes([FromQuery] Guid? folderId)
    {
        var query = _db.Notes.Where(n => n.UserId == UserId && !n.IsDeleted);
        if (folderId.HasValue)
        {
            query = query.Where(n => n.FolderId == folderId.Value);
        }

        var notes = await query
            .OrderByDescending(n => n.UpdatedAt)
            .Select(n => new NoteListItemDto
            {
                Id = n.Id, Title = n.Title, FolderId = n.FolderId,
                IsFavorite = n.IsFavorite, IsPinned = n.IsPinned,
                IsDeleted = n.IsDeleted, Version = n.Version, UpdatedAt = n.UpdatedAt
            })
            .ToListAsync();
        return Ok(notes);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NoteDto>> GetNote(Guid id)
    {
        var note = await _db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.UserId == UserId && !n.IsDeleted);
        if (note == null)
        {
            return ApiProblemResults.NotFound("笔记不存在或无权访问", HttpContext);
        }

        return Ok(new NoteDto
        {
            Id = note.Id, Title = note.Title, Content = note.Content,
            FolderId = note.FolderId, IsFavorite = note.IsFavorite, IsPinned = note.IsPinned,
            IsDeleted = note.IsDeleted,
            Version = note.Version, CreatedAt = note.CreatedAt, UpdatedAt = note.UpdatedAt
        });
    }

    [HttpPost]
    public async Task<ActionResult<NoteDto>> CreateNote([FromBody] CreateNoteRequest request)
    {
        var result = await _noteMutations.CreateAsync(UserId, request);
        return MapMutationResult(result, success: r => Ok(r.Note));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateNote(Guid id, [FromBody] UpdateNoteRequest request)
    {
        var result = await _noteMutations.UpdateAsync(UserId, id, request);
        return MapMutationResult(result, success: _ => NoContent());
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteNote(Guid id)
    {
        var result = await _noteMutations.DeleteAsync(UserId, id);
        return MapMutationResult(result, success: _ => NoContent());
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
}

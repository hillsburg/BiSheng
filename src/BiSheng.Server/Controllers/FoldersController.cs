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
public class FoldersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IFolderMutationService _folderMutations;

    /// <summary>构造文件夹控制器</summary>
    public FoldersController(AppDbContext db, IFolderMutationService folderMutations)
    {
        _db = db;
        _folderMutations = folderMutations;
    }

    private Guid UserId => User.GetUserId();

    [HttpGet]
    public async Task<ActionResult<List<FolderDto>>> GetFolders()
    {
        var folders = await _db.Folders
            .Where(f => f.UserId == UserId && !f.IsDeleted)
            .Select(f => new FolderDto
            {
                Id = f.Id, Name = f.Name, ParentId = f.ParentId,
                IsFavorite = f.IsFavorite, IsPinned = f.IsPinned,
                IsDeleted = f.IsDeleted, Version = f.Version,
                CreatedAt = f.CreatedAt, UpdatedAt = f.UpdatedAt
            })
            .ToListAsync();
        return Ok(folders);
    }

    [HttpPost]
    public async Task<ActionResult<FolderDto>> CreateFolder([FromBody] CreateFolderRequest request)
    {
        var result = await _folderMutations.CreateAsync(UserId, request);
        return MapMutationResult(result, success: r => CreatedAtAction(nameof(GetFolders), r.Folder));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateFolder(Guid id, [FromBody] UpdateFolderRequest request)
    {
        var result = await _folderMutations.UpdateAsync(UserId, id, request);
        return MapMutationResult(result, success: _ => NoContent());
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteFolder(Guid id)
    {
        var result = await _folderMutations.DeleteAsync(UserId, id);
        return MapMutationResult(result, success: _ => NoContent());
    }

    /// <summary>将 MutationResult 映射为 HTTP 响应</summary>
    private ActionResult MapMutationResult(
        FolderMutationResult result,
        Func<FolderMutationResult, ActionResult> success)
    {
        if (result.Outcome == MutationOutcome.Success)
        {
            return success(result);
        }

        return ApiProblemResults.FromMutation(result.Outcome, result.ErrorMessage, HttpContext);
    }
}

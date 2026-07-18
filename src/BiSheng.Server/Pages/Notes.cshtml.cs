using BiSheng.Server.Auth;
using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using BiSheng.Server.Services.Images;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Pages;

[AdminPanelAuthorize]
public class NotesModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    /// <summary>构造管理面板笔记页</summary>
    public NotesModel(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    /// <summary>
    /// 当前选中的文件夹 ID（null 表示全部）
    /// </summary>
    public Guid? FilterFolderId { get; set; }

    /// <summary>
    /// 当前查看的笔记详情
    /// </summary>
    public Note? SelectedNote { get; set; }

    /// <summary>
    /// 笔记列表
    /// </summary>
    public List<NoteListItem> Notes { get; set; } = new();

    /// <summary>
    /// 文件夹列表（用于筛选下拉框）
    /// </summary>
    public List<FolderOption> Folders { get; set; } = new();

    /// <summary>
    /// 是否显示已删除的笔记
    /// </summary>
    public bool ShowDeleted { get; set; }

    /// <summary>
    /// 当前笔记关联的图片列表
    /// </summary>
    public List<ServerImage> NoteImages { get; set; } = new();

    public record NoteListItem(
        Guid Id, string Title, Guid FolderId, string FolderName,
        bool IsDeleted, long Version, DateTime UpdatedAt);

    public record FolderOption(Guid Id, string Name);

    public async Task OnGetAsync(Guid? folderId, Guid? noteId, bool showDeleted = false)
    {
        FilterFolderId = folderId;
        ShowDeleted = showDeleted;

        var userId = User.GetUserId();

        // 加载文件夹列表（用于筛选下拉）
        Folders = await _db.Folders
            .Where(f => f.UserId == userId && !f.IsDeleted)
            .OrderBy(f => f.Name)
            .Select(f => new FolderOption(f.Id, f.Name))
            .ToListAsync();

        // 加载文件夹名称映射
        var folderNames = Folders.ToDictionary(f => f.Id, f => f.Name);

        // 查询笔记
        var query = _db.Notes.Where(n => n.UserId == userId);
        if (!showDeleted)
            query = query.Where(n => !n.IsDeleted);
        if (folderId.HasValue)
            query = query.Where(n => n.FolderId == folderId.Value);

        Notes = await query
            .OrderByDescending(n => n.UpdatedAt)
            .Select(n => new NoteListItem(
                n.Id, n.Title, n.FolderId,
                string.Empty, // 后续填充
                n.IsDeleted, n.Version, n.UpdatedAt))
            .ToListAsync();

        // 填充文件夹名称
        Notes = Notes.Select(n => n with
        {
            FolderName = folderNames.GetValueOrDefault(n.FolderId, "未知文件夹")
        }).ToList();

        // 加载笔记详情
        if (noteId.HasValue)
        {
            SelectedNote = await _db.Notes
                .FirstOrDefaultAsync(n => n.Id == noteId.Value && n.UserId == userId);

            // 解析笔记内容中的图片引用（Markdown 为唯一真相源）
            if (SelectedNote != null && !string.IsNullOrEmpty(SelectedNote.Content))
            {
                var imageIds = NoteImageReferenceScanner.ExtractImageIds(SelectedNote.Content);
                if (imageIds.Count > 0)
                {
                    NoteImages = await _db.Images
                        .Where(i => imageIds.Contains(i.Id) && i.UserId == userId)
                        .OrderBy(i => i.CreatedAt)
                        .ToListAsync();
                }
            }
        }
    }

    /// <summary>
    /// 为管理面板提供图片访问端点（Cookie 认证）
    /// URL: /admin/notes?handler=Image&id={imageId}
    /// </summary>
    public async Task<IActionResult> OnGetImageAsync(Guid id)
    {
        var userId = User.GetUserId();
        var image = await _db.Images
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);

        if (image == null || image.IsDeleted)
            return NotFound();

        var filePath = Path.Combine(_env.ContentRootPath, "uploads",
            userId.ToString(), $"{image.Id}{image.Extension}");

        if (!System.IO.File.Exists(filePath))
            return NotFound();

        return PhysicalFile(filePath, image.ContentType, image.FileName);
    }
}

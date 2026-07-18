using BiSheng.Server.Auth;
using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Pages;

[AdminPanelAuthorize]
public class FoldersModel : PageModel
{
    private readonly AppDbContext _db;

    public FoldersModel(AppDbContext db) => _db = db;

    /// <summary>
    /// 文件夹树节点（扁平化，带层级深度）
    /// </summary>
    public record FolderTreeNode(
        Guid Id, string Name, Guid? ParentId,
        int Depth, int NoteCount, int ActiveNoteCount,
        DateTime CreatedAt, DateTime UpdatedAt, bool IsDeleted);

    public List<FolderTreeNode> Tree { get; set; } = new();

    /// <summary>
    /// 当前选中的文件夹（显示其下的笔记列表）
    /// </summary>
    public Folder? SelectedFolder { get; set; }
    public List<FolderNoteItem> SelectedFolderNotes { get; set; } = new();

    public record FolderNoteItem(Guid Id, string Title, bool IsDeleted, DateTime UpdatedAt);

    public async Task OnGetAsync(Guid? folderId)
    {
        var userId = User.GetUserId();

        // 查询所有文件夹
        var allFolders = await _db.Folders
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.Name)
            .ToListAsync();

        // 查询每个文件夹的笔记数量
        var noteCounts = await _db.Notes
            .Where(n => n.UserId == userId)
            .GroupBy(n => n.FolderId)
            .Select(g => new
            {
                FolderId = g.Key,
                Total = g.Count(),
                Active = g.Count(n => !n.IsDeleted)
            })
            .ToListAsync();
        var noteCountMap = noteCounts.ToDictionary(x => x.FolderId, x => (x.Total, x.Active));

        // 构建树
        Tree = BuildTree(allFolders, noteCountMap, parentId: null, depth: 0);

        // 如果选中了某个文件夹，加载其笔记
        if (folderId.HasValue)
        {
            SelectedFolder = allFolders.FirstOrDefault(f => f.Id == folderId.Value);
            if (SelectedFolder != null)
            {
                SelectedFolderNotes = await _db.Notes
                    .Where(n => n.FolderId == folderId.Value && n.UserId == userId)
                    .OrderByDescending(n => n.UpdatedAt)
                    .Select(n => new FolderNoteItem(n.Id, n.Title, n.IsDeleted, n.UpdatedAt))
                    .ToListAsync();
            }
        }
    }

    private static List<FolderTreeNode> BuildTree(
        List<Folder> folders,
        Dictionary<Guid, (int Total, int Active)> noteCountMap,
        Guid? parentId, int depth)
    {
        var result = new List<FolderTreeNode>();
        var children = folders.Where(f => f.ParentId == parentId).ToList();

        foreach (var folder in children)
        {
            var (total, active) = noteCountMap.GetValueOrDefault(folder.Id, (0, 0));
            result.Add(new FolderTreeNode(
                folder.Id, folder.Name, folder.ParentId,
                depth, total, active,
                folder.CreatedAt, folder.UpdatedAt, folder.IsDeleted));

            // 递归子文件夹
            result.AddRange(BuildTree(folders, noteCountMap, folder.Id, depth + 1));
        }

        return result;
    }
}

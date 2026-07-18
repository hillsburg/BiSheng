using BiSheng.Server.Auth;
using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Pages;

[AdminPanelAuthorize]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db) => _db = db;

    public string AdminUsername { get; set; } = string.Empty;
    public int NoteCount { get; set; }
    public int FolderCount { get; set; }
    public int ActiveKeyCount { get; set; }
    public DateTime? SetupTime { get; set; }
    public List<ApiKey> RecentKeys { get; set; } = new();

    public async Task OnGetAsync()
    {
        AdminUsername = User.Identity?.Name ?? "Unknown";
        NoteCount = await _db.Notes.CountAsync();
        FolderCount = await _db.Folders.CountAsync();
        ActiveKeyCount = await _db.ApiKeys.CountAsync(k => k.IsActive);
        SetupTime = await _db.ServerConfigs.Where(c => c.Id == 1).Select(c => c.SetupAt).FirstOrDefaultAsync();
        RecentKeys = await _db.ApiKeys
            .Where(k => k.IsActive)
            .OrderByDescending(k => k.CreatedAt)
            .Take(5)
            .ToListAsync();
    }
}

using System.Globalization;
using System.Reflection;
using BiSheng.Server.Auth;
using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Pages;

[AdminPanelAuthorize]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;

    public IndexModel(AppDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public string AdminUsername { get; set; } = string.Empty;
    public string ServerVersion { get; set; } = string.Empty;
    public int NoteCount { get; set; }
    public int DeletedNoteCount { get; set; }
    public int FolderCount { get; set; }
    public int ActiveKeyCount { get; set; }
    public string DatabaseSizeDisplay { get; set; } = "未知";
    public string? LastClientActivityDisplay { get; set; }
    public DateTime? SetupTime { get; set; }
    public List<ApiKey> RecentKeys { get; set; } = new();

    public async Task OnGetAsync()
    {
        AdminUsername = User.Identity?.Name ?? "Unknown";
        ServerVersion = ResolveServerVersion();
        NoteCount = await _db.Notes.CountAsync(n => !n.IsDeleted);
        DeletedNoteCount = await _db.Notes.CountAsync(n => n.IsDeleted);
        FolderCount = await _db.Folders.CountAsync(f => !f.IsDeleted);
        ActiveKeyCount = await _db.ApiKeys.CountAsync(k => k.IsActive);
        SetupTime = await _db.ServerConfigs.Where(c => c.Id == 1).Select(c => c.SetupAt).FirstOrDefaultAsync();
        DatabaseSizeDisplay = ResolveDatabaseSizeDisplay();
        LastClientActivityDisplay = await ResolveLastClientActivityDisplayAsync();
        RecentKeys = await _db.ApiKeys
            .Where(k => k.IsActive)
            .OrderByDescending(k => k.LastUsedAt ?? k.CreatedAt)
            .Take(5)
            .ToListAsync();
    }

    private static string ResolveServerVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // 去掉可能附带的 git commit 后缀
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }

        return asm.GetName().Version?.ToString(3) ?? "unknown";
    }

    private string ResolveDatabaseSizeDisplay()
    {
        var cs = _configuration.GetConnectionString("DefaultConnection")
            ?? ServerDatabasePaths.DefaultConnectionString;
        var path = TryResolveSqlitePath(cs);
        if (path == null || !System.IO.File.Exists(path))
        {
            return "未知";
        }

        var bytes = new FileInfo(path).Length;
        return FormatBytes(bytes);
    }

    private async Task<string?> ResolveLastClientActivityDisplayAsync()
    {
        var lastUsed = await _db.ApiKeys
            .Where(k => k.LastUsedAt != null)
            .MaxAsync(k => (DateTime?)k.LastUsedAt);
        var lastSeen = await _db.ClientSyncStates
            .MaxAsync(s => (DateTime?)s.LastSeenAt);

        DateTime? latest = null;
        if (lastUsed.HasValue)
        {
            latest = lastUsed;
        }

        if (lastSeen.HasValue && (!latest.HasValue || lastSeen > latest))
        {
            latest = lastSeen;
        }

        return latest.HasValue ? FormatUtcLocal(latest.Value) : null;
    }

    private static string? TryResolveSqlitePath(string connectionString)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            var dataSource = builder.DataSource;
            if (string.IsNullOrWhiteSpace(dataSource))
            {
                return null;
            }

            if (System.IO.File.Exists(dataSource))
            {
                return Path.GetFullPath(dataSource);
            }

            var underBase = Path.Combine(AppContext.BaseDirectory, Path.GetFileName(dataSource));
            if (System.IO.File.Exists(underBase))
            {
                return underBase;
            }

            var underCwd = Path.GetFullPath(dataSource);
            return System.IO.File.Exists(underCwd) ? underCwd : null;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} {units[unit]}"
            : string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", size, units[unit]);
    }

    /// <summary>UTC 时间格式化为本地可读字符串</summary>
    internal static string FormatUtcLocal(DateTime utc)
    {
        var local = utc.Kind == DateTimeKind.Utc
            ? utc.ToLocalTime()
            : DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        return local.ToString("yyyy-MM-dd HH:mm");
    }
}

using BiSheng.Server.Auth;
using BiSheng.Server.Data;using BiSheng.Server.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Pages;

[AdminPanelAuthorize]
public class KeysModel : PageModel
{
    private readonly AppDbContext _db;

    public KeysModel(AppDbContext db) => _db = db;

    [BindProperty] public string NewDeviceName { get; set; } = string.Empty;
    public List<ApiKey> Keys { get; set; } = new();
    public string? NewKeyValue { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;

    public async Task OnGetAsync()
    {
        // 从 TempData 读取刚生成的 Key（仅显示一次）
        NewKeyValue = TempData["NewApiKey"] as string;

        var userId = User.GetUserId();
        Keys = await _db.ApiKeys
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewDeviceName))
        {
            ErrorMessage = "请输入设备名称";
            return await ReloadPage();
        }

        var userId = User.GetUserId();
        var keyValue = SetupModel.GenerateApiKey();

        _db.ApiKeys.Add(new ApiKey
        {
            KeyValue = ApiKeyAuthHandler.HashApiKey(keyValue),  // 存储哈希值
            DeviceName = NewDeviceName.Trim(),
            UserId = userId,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        TempData["NewApiKey"] = keyValue;
        return Redirect("/admin/keys");
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid keyId)
    {
        var userId = User.GetUserId();
        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.UserId == userId);
        if (key != null)
        {
            key.IsActive = false;
            await _db.SaveChangesAsync();
        }
        return Redirect("/admin/keys");
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid keyId)
    {
        var userId = User.GetUserId();
        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.UserId == userId);
        if (key != null)
        {
            _db.ApiKeys.Remove(key);
            await _db.SaveChangesAsync();
        }
        return Redirect("/admin/keys");
    }

    private async Task<PageResult> ReloadPage()
    {
        await OnGetAsync();
        return Page();
    }
}

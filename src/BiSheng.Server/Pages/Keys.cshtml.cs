using BiSheng.Server.Auth;
using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Pages;

[AdminPanelAuthorize]
public class KeysModel : PageModel
{
    private readonly AppDbContext _db;

    public KeysModel(AppDbContext db) => _db = db;

    [BindProperty]
    public string NewDeviceName { get; set; } = string.Empty;

    [BindProperty]
    public Guid RenameKeyId { get; set; }

    [BindProperty]
    public string RenameDeviceName { get; set; } = string.Empty;

    public List<ApiKey> Keys { get; set; } = new();
    public string? NewKeyValue { get; set; }
    public string? NewKeyDeviceName { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string SuccessMessage { get; set; } = string.Empty;

    public async Task OnGetAsync()
    {
        // 从 TempData 读取刚生成的 Key（仅显示一次）
        NewKeyValue = TempData["NewApiKey"] as string;
        NewKeyDeviceName = TempData["DeviceName"] as string;
        SuccessMessage = TempData["SuccessMessage"] as string ?? string.Empty;

        var userId = User.GetUserId();
        Keys = await _db.ApiKeys
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.IsActive)
            .ThenByDescending(k => k.LastUsedAt ?? k.CreatedAt)
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
            KeyValue = ApiKeyAuthHandler.HashApiKey(keyValue),
            DeviceName = NewDeviceName.Trim(),
            UserId = userId,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        TempData["NewApiKey"] = keyValue;
        TempData["DeviceName"] = NewDeviceName.Trim();
        return Redirect("/admin/keys");
    }

    public async Task<IActionResult> OnPostRenameAsync()
    {
        if (string.IsNullOrWhiteSpace(RenameDeviceName))
        {
            ErrorMessage = "设备名称不能为空";
            return await ReloadPage();
        }

        var userId = User.GetUserId();
        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == RenameKeyId && k.UserId == userId);
        if (key == null)
        {
            ErrorMessage = "未找到该 Key";
            return await ReloadPage();
        }

        key.DeviceName = RenameDeviceName.Trim();
        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = "设备名称已更新";
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

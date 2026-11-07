using BiSheng.Server.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BiSheng.Server.Pages;

public class LogoutModel(AdminSessionService sessions, AdminPendingSessionService pending) : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        pending.ClearLoginPending(HttpContext);
        pending.ClearTotpRebindPending(HttpContext);
        await sessions.SignOutAsync(HttpContext);
        return Redirect("/admin/login");
    }
}

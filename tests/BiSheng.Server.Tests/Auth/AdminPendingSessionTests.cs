using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using BiSheng.Server.Auth;
using BiSheng.Server.Services;
using BiSheng.Server.Tests.Fixtures;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OtpNet;

namespace BiSheng.Server.Tests.Auth;

/// <summary>
/// PR1：管理后台两步登录 / 初始化短时会话安全
/// </summary>
public class AdminPendingSessionTests
{
    /// <summary>加密 Cookie 往返成功；篡改后读取失败</summary>
    [Fact]
    public void LoginPending_RoundTrip_AndRejectTamperedCookie()
    {
        var service = CreateSessionService();
        var context = new DefaultHttpContext();
        var userId = Guid.NewGuid();

        service.SetLoginPending(context, userId, "admin");

        var setCookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains(AdminPendingSessionService.LoginCookieName, setCookie);

        var request = new DefaultHttpContext();
        request.Request.Headers.Cookie = ExtractCookieHeader(setCookie);

        Assert.True(service.TryGetLoginPending(request, out var payload));
        Assert.Equal(userId, payload.UserId);
        Assert.Equal("admin", payload.Username);

        request.Request.Headers.Cookie =
            $"{AdminPendingSessionService.LoginCookieName}=not-a-valid-payload";
        Assert.False(service.TryGetLoginPending(request, out _));
    }

    /// <summary>无 pending Cookie 时，伪造 UserId + 正确 TOTP 不能签发管理 Cookie</summary>
    [Fact]
    public async Task Login_ForgedPendingUserId_WithoutSession_DoesNotSignIn()
    {
        using var factory = new BiShengWebAppFactory();
        var client = CreateClient(factory);
        var (_, userId, _, totpSecret) = await SeedAdminAsync(factory, "admin", "Password1!");

        var token = await GetAntiforgeryTokenAsync(client, "/admin/login");
        var response = await client.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["PendingUserId"] = userId.ToString(),
            ["TotpCode"] = GenerateTotpCode(totpSecret)
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(HasAuthCookie(response));
        var body = await response.Content.ReadAsStringAsync();
        // 无会话时按「第一步」处理，不应进入 TOTP 成功登录
        Assert.DoesNotContain("验证并登录", body);
        Assert.Contains("管理员登录", body);
    }

    /// <summary>合法两步：密码 → pending Cookie → 正确 TOTP → 登录成功</summary>
    [Fact]
    public async Task Login_TwoStep_WithValidTotp_SignsIn()
    {
        using var factory = new BiShengWebAppFactory();
        var client = CreateClient(factory);
        var (_, _, _, totpSecret) = await SeedAdminAsync(factory, "admin", "Password1!");

        var token1 = await GetAntiforgeryTokenAsync(client, "/admin/login");
        var step1 = await client.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token1,
            ["Username"] = "admin",
            ["Password"] = "Password1!"
        }));
        Assert.Equal(HttpStatusCode.OK, step1.StatusCode);
        Assert.True(HasCookie(step1, AdminPendingSessionService.LoginCookieName));
        var step1Html = await step1.Content.ReadAsStringAsync();
        Assert.Contains("验证并登录", step1Html);

        var token2 = await GetAntiforgeryTokenAsync(client, "/admin/login");
        var step2 = await client.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token2,
            ["TotpCode"] = GenerateTotpCode(totpSecret)
        }));

        Assert.Equal(HttpStatusCode.Redirect, step2.StatusCode);
        Assert.Equal("/admin", step2.Headers.Location?.OriginalString);
        Assert.True(HasAuthCookie(step2));
    }

    /// <summary>Setup：仅提交伪造 TotpSecret 不能完成初始化（无 pending 会话）</summary>
    [Fact]
    public async Task Setup_ClientSuppliedTotpSecret_WithoutSession_DoesNotComplete()
    {
        using var factory = new BiShengWebAppFactory();
        var client = CreateClient(factory);

        var forgedSecret = TotpHelper.GenerateSecret();
        var token = await GetAntiforgeryTokenAsync(client, "/admin/setup");
        var response = await client.PostAsync("/admin/setup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Username"] = "attacker",
            ["Password"] = "Password1!",
            ["ConfirmPassword"] = "Password1!",
            ["TotpSecret"] = forgedSecret,
            ["TotpCode"] = GenerateTotpCode(forgedSecret)
        }));

        // 无 pending-setup → 走第一步，服务端自行生成密钥并进入绑定页
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("绑定验证器", body);
        Assert.True(HasCookie(response, AdminPendingSessionService.SetupCookieName));

        await using var check = factory.NewDbContext();
        Assert.False(await check.ServerConfigs.AnyAsync(c => c.Id == 1 && c.IsSetup));
        Assert.Empty(await check.Users.ToListAsync());
    }

    /// <summary>创建会话服务（独立 DataProtection）</summary>
    private static AdminPendingSessionService CreateSessionService()
    {
        var provider = DataProtectionProvider.Create(nameof(AdminPendingSessionTests));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cookies:SecureAlways"] = "false"
            })
            .Build();
        return new AdminPendingSessionService(provider, configuration);
    }

    /// <summary>创建不自动跟随重定向的测试客户端（仍处理 Cookie）</summary>
    private static HttpClient CreateClient(BiShengWebAppFactory factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    /// <summary>种子管理员（真实密码哈希 + TOTP 密钥）</summary>
    private static async Task<(string plaintextApiKey, Guid userId, Guid folderId, string totpSecret)> SeedAdminAsync(
        BiShengWebAppFactory factory,
        string username,
        string password)
    {
        var totpSecret = TotpHelper.GenerateSecret();
        var result = await factory.SeedAsync(currentVersion: 1);

        await using var db = factory.NewDbContext();
        var user = await db.Users.SingleAsync(u => u.Id == result.userId);
        user.Username = username;
        user.PasswordHash = new PasswordHasher<object>().HashPassword(null!, password);
        user.TotpSecret = totpSecret;
        await db.SaveChangesAsync();
        return (result.plaintextApiKey, result.userId, result.folderId, totpSecret);
    }

    /// <summary>生成当前 TOTP 码</summary>
    private static string GenerateTotpCode(string secret)
    {
        var totp = new Totp(Base32Encoding.ToBytes(secret));
        return totp.ComputeTotp();
    }

    /// <summary>GET 页面并解析 AntiForgery token</summary>
    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!match.Success)
        {
            match = Regex.Match(
                html,
                "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"");
        }

        Assert.True(match.Success, "页面中未找到 AntiForgery token");
        return match.Groups[1].Value;
    }

    /// <summary>响应是否签发了管理认证 Cookie</summary>
    private static bool HasAuthCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return false;
        }

        return cookies.Any(c =>
            c.StartsWith("bisheng.admin=", StringComparison.Ordinal)
            && !c.StartsWith("bisheng.admin.pending", StringComparison.Ordinal));
    }

    /// <summary>响应是否包含指定 Cookie</summary>
    private static bool HasCookie(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return false;
        }

        return cookies.Any(c => c.StartsWith(name + "=", StringComparison.Ordinal));
    }

    /// <summary>从 Set-Cookie 头拼 Request Cookie</summary>
    private static string ExtractCookieHeader(string setCookieHeader)
    {
        var pairs = new List<string>();
        foreach (var part in setCookieHeader.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var nv = part.Split(';')[0].Trim();
            if (nv.Contains('='))
            {
                pairs.Add(nv);
            }
        }

        if (pairs.Count == 0)
        {
            var nv = setCookieHeader.Split(';')[0].Trim();
            if (nv.Contains('='))
            {
                pairs.Add(nv);
            }
        }

        return string.Join("; ", pairs);
    }
}

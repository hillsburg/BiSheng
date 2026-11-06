using Microsoft.Extensions.DependencyInjection;
using System.Formats.Cbor;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BiSheng.Server.Auth;
using BiSheng.Server.Tests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using OtpNet;

namespace BiSheng.Server.Tests.Auth;

/// <summary>使用真实 P-256 签名的软件认证器，覆盖完整 WebAuthn 仪式及拒绝路径。</summary>
public sealed class PasskeyTests
{
    [Theory]
    [InlineData("example.com", "https://example.com", true)]
    [InlineData("example.com", "https://admin.example.com", true)]
    [InlineData("localhost", "http://localhost", true)]
    [InlineData("example.com", "http://example.com", false)]
    [InlineData("example.com", "https://evil.example", false)]
    [InlineData("example.com", "https://example.com/admin", false)]
    [InlineData("example.com", "https://example.com/", false)]
    [InlineData("example.com", "https://user@example.com", false)]
    [InlineData("*.example.com", "https://admin.example.com", false)]
    public void ConfigurationEnforcesExactTrustBoundary(string rp, string origin, bool expected)
    {
        Assert.Equal(expected, new WebAuthnOptions { Enabled = true, RpId = rp, Origins = [origin] }.IsValid());
    }

    /// <summary>两种入口使用不同的认证器选择参数，均保持可发现凭据与 UV 要求。</summary>
    [Theory]
    [InlineData(false, "platform")]
    [InlineData(true, "cross-platform")]
    public async Task RegistrationSelectsRequestedAuthenticator(bool useOtherDevice, string attachment)
    {
        using var factory = new BiShengWebAppFactory { EnablePasskeys = true };
        await factory.SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BiSheng.Server.Data.AppDbContext>();
        var user = await db.Users.SingleAsync();
        var service = scope.ServiceProvider.GetRequiredService<PasskeyService>();
        using var options = JsonDocument.Parse(await service.BeginRegistrationAsync(
            new Microsoft.AspNetCore.Http.DefaultHttpContext(), user, "test", useOtherDevice));
        var selection = options.RootElement.GetProperty("authenticatorSelection");
        Assert.Equal(attachment, selection.GetProperty("authenticatorAttachment").GetString());
        Assert.Equal("required", selection.GetProperty("residentKey").GetString());
        Assert.Equal("required", selection.GetProperty("userVerification").GetString());
    }

    [Fact]
    public async Task RegisterLoginRenameDeleteAndReplayProtection()
    {
        using var factory = new BiShengWebAppFactory { EnablePasskeys = true };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (_, userId, _) = await factory.SeedAsync();
        var secret = BiSheng.Server.Services.TotpHelper.GenerateSecret();
        await using (var db = factory.NewDbContext())
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.Username = "admin";
            user.PasswordHash = PasswordCredentialService.HashPassword("Password1!");
            user.TotpSecret = secret;
            await db.SaveChangesAsync();
        }
        await PasswordLogin(client, secret);
        using var authenticator = new SoftwareAuthenticator();
        var options = await Post(client, "/register/options", new { name = "测试设备", password = "Password1!" });
        Assert.Equal("required", options.GetProperty("authenticatorSelection").GetProperty("userVerification").GetString());
        var attestation = authenticator.Attest(options);
        var registered = await Send(client, "/register/complete", attestation);
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        var replay = await Send(client, "/register/complete", attestation);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        await using (var db = factory.NewDbContext())
        {
            Assert.Equal(userId, (await db.PasskeyCredentials.SingleAsync()).UserId);
        }

        // 新浏览器完成无用户名登录；每个浏览器使用独立来源 IP，避免共享限流预算。
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        browser.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.2");
        var assertionOptions = await Post(browser, "/login/options", new { });
        var assertion = authenticator.Assert(assertionOptions, userId);
        var login = await Send(browser, "/login/complete", assertion);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/admin/account")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(browser, "/login/complete", assertion)).StatusCode);

        var list = await browser.GetFromJsonAsync<JsonElement>("/api/admin/passkeys");
        var id = list[0].GetProperty("id").GetGuid();
        Assert.False(list[0].TryGetProperty("publicKey", out _));
        // 重命名只改显示文本，不需要重新授权；删除仍需当前密码。管理操作换用新 IP，避开登录阶段已用的限流预算。
        browser.DefaultRequestHeaders.Remove("X-Forwarded-For");
        browser.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.3");
        Assert.Equal(HttpStatusCode.OK, (await Send(browser, "/" + id + "/rename", new { name = "新名称" })).StatusCode);
        Assert.Equal("新名称", (await browser.GetFromJsonAsync<JsonElement>("/api/admin/passkeys"))[0].GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(browser, "/" + id + "/delete", new { password = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(browser, "/" + id + "/delete", new { password = "Password1!" })).StatusCode);
        await using (var db = factory.NewDbContext())
        {
            Assert.Empty(await db.PasskeyCredentials.ToListAsync());
        }
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/admin/account")).StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example", true)]
    [InlineData("http://localhost", false)]
    public async Task RegistrationRejectsWrongOriginOrMissingUv(string origin, bool uv)
    {
        using var factory = new BiShengWebAppFactory { EnablePasskeys = true };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await factory.SeedAsync();
        // 注册必须先认证，不能通过 API Key 或匿名请求绑定。
        Assert.Equal(HttpStatusCode.Redirect, (await Send(client, "/register/options", new { name = "test", password = "x" })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BiSheng.Server.Data.AppDbContext>();
        var user = await db.Users.SingleAsync();
        var pending = scope.ServiceProvider.GetRequiredService<AdminPendingSessionService>();
        var service = scope.ServiceProvider.GetRequiredService<PasskeyService>();
        var start = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        using var options = JsonDocument.Parse(await service.BeginRegistrationAsync(start, user, "test"));
        var finish = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        finish.Request.Headers.Cookie = start.Response.Headers.SetCookie.ToString().Split(';')[0];
        using var authenticator = new SoftwareAuthenticator();
        var raw = JsonSerializer.Deserialize<Fido2NetLib.AuthenticatorAttestationRawResponse>(
            JsonSerializer.Serialize(authenticator.Attest(options.RootElement, origin, uv)), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        await Assert.ThrowsAsync<PasskeyRejectedException>(() => service.FinishRegistrationAsync(finish, user, raw));
        Assert.Empty(await db.PasskeyCredentials.ToListAsync());
        Assert.False(pending.TakePasskeyRegisterPending(finish, out _));
    }

    [Theory]
    [InlineData("https://evil.example", true, false, false)]
    [InlineData("http://localhost", false, false, false)]
    [InlineData("http://localhost", true, true, false)]
    [InlineData("http://localhost", true, false, true)]
    public async Task AssertionRejectsWrongOriginMissingUvBadSignatureOrWrongUser(string origin, bool uv, bool corrupt, bool wrongUser)
    {
        using var factory = new BiShengWebAppFactory { EnablePasskeys = true };
        await factory.SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BiSheng.Server.Data.AppDbContext>();
        var user = await db.Users.SingleAsync();
        var service = scope.ServiceProvider.GetRequiredService<PasskeyService>();
        var pending = scope.ServiceProvider.GetRequiredService<AdminPendingSessionService>();
        using var authenticator = new SoftwareAuthenticator();
        var start = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        using var registration = JsonDocument.Parse(await service.BeginRegistrationAsync(start, user, "test"));
        // 绑定成功会换发 Cookie，需要认证服务。
        var finish = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = scope.ServiceProvider };
        finish.Request.Headers.Cookie = start.Response.Headers.SetCookie.ToString().Split(';')[0];
        var raw = JsonSerializer.Deserialize<Fido2NetLib.AuthenticatorAttestationRawResponse>(
            JsonSerializer.Serialize(authenticator.Attest(registration.RootElement)), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        await service.FinishRegistrationAsync(finish, user, raw);
        start = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        using var options = JsonDocument.Parse(service.BeginLogin(start));
        finish = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        finish.Request.Headers.Cookie = start.Response.Headers.SetCookie.ToString().Split(';')[0];
        var assertion = JsonSerializer.Deserialize<Fido2NetLib.AuthenticatorAssertionRawResponse>(
            JsonSerializer.Serialize(authenticator.Assert(options.RootElement, wrongUser ? Guid.NewGuid() : user.Id, origin, uv, corrupt)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        await Assert.ThrowsAsync<PasskeyRejectedException>(() => service.FinishLoginAsync(finish, assertion));
        Assert.False(pending.TakePasskeyLoginPending(finish, out _));
        Assert.Null((await db.PasskeyCredentials.SingleAsync()).LastUsedAtUtc);
    }

    /// <summary>页面脚本必须能由实际 HTTP 管线提供，防止表单退回无提示的原生提交。</summary>
    [Theory]
    [InlineData("/js/passkey-common.js", "BiShengPasskey")]
    [InlineData("/js/passkey-login.js", "navigator.credentials.get")]
    [InlineData("/js/passkey-manage.js", "navigator.credentials.create")]
    public async Task PasskeyScriptIsServedWithoutAuthentication(string path, string marker)
    {
        using var factory = new BiShengWebAppFactory { EnablePasskeys = true };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(marker, await response.Content.ReadAsStringAsync());
    }

    /// <summary>绑定或删除后换发 Cookie 不得延长免 TOTP 窗口。</summary>
    [Fact]
    public async Task RefreshKeepsOriginalVerificationTime()
    {
        var auth = new CapturingAuthenticationService();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(auth).BuildServiceProvider(),
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(AdminSessionService.VerifiedAtClaim, "12345") }, "test"))
        };
        await new AdminSessionService(null!).RefreshAsync(context, new BiSheng.Server.Data.Entities.User { Username = "admin", AuthVersion = 2 });
        Assert.Equal("12345", auth.Principal!.FindFirstValue(AdminSessionService.VerifiedAtClaim));
        Assert.Equal("2", auth.Principal!.FindFirstValue(AdminSessionService.VersionClaim));
    }

    [Fact]
    public async Task LoginEndpointsEnforceAntiforgeryAndRateLimit()
    {
        using var factory = new BiShengWebAppFactory { EnablePasskeys = true };
        await factory.SeedAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/admin/passkeys/login/options", new { })).StatusCode);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Send(client, "/login/options", new { })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(client, "/login/options", new { })).StatusCode);
    }

    /// <summary>获取真实页面生成的防伪令牌，模拟浏览器携带 Cookie 的 JSON 请求。</summary>
    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, object body)
    {
        var page = await client.GetAsync("/admin/account");
        var html = page.StatusCode == HttpStatusCode.Redirect
            ? await client.GetStringAsync("/admin/login") : await page.Content.ReadAsStringAsync();
        // 匿名注册的授权检查发生在防伪校验前，无需 account 页面返回有效令牌。
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/passkeys" + path) { Content = JsonContent.Create(body) };
        request.Headers.Add("RequestVerificationToken", match.Success ? match.Groups[1].Value : "invalid");
        return await client.SendAsync(request);
    }

    /// <summary>要求 options 接口成功并读取协议 JSON。</summary>
    private static async Task<JsonElement> Post(HttpClient client, string path, object body)
    {
        var response = await Send(client, path, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>通过原有密码与 TOTP 登录，为凭据管理建立近期验证会话。</summary>
    private static async Task PasswordLogin(HttpClient client, string secret)
    {
        var html = await client.GetStringAsync("/admin/login");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        await client.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["Username"] = "admin", ["Password"] = "Password1!"
        }));
        var result = await client.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["TotpCode"] = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp()
        }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
    }

    /// <summary>只记录签发的身份，不写 Cookie。</summary>
    private sealed class CapturingAuthenticationService : IAuthenticationService
    {
        public ClaimsPrincipal? Principal { get; private set; }
        public Task<AuthenticateResult> AuthenticateAsync(Microsoft.AspNetCore.Http.HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(Microsoft.AspNetCore.Http.HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(Microsoft.AspNetCore.Http.HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(Microsoft.AspNetCore.Http.HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            Principal = principal;
            return Task.CompletedTask;
        }
        public Task SignOutAsync(Microsoft.AspNetCore.Http.HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    }

    /// <summary>测试用认证器构造 none attestation 和真实 ES256 assertion，不模拟服务端验证器。</summary>
    private sealed class SoftwareAuthenticator : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly byte[] _id = RandomNumberGenerator.GetBytes(32);
        private static string B64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static byte[] ClientData(JsonElement options, string type, string origin) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            type, challenge = options.GetProperty("challenge").GetString(), origin, crossOrigin = false
        });

        /// <summary>构造携带 COSE 公钥的 none 注册证明。</summary>
        public object Attest(JsonElement options, string origin = "http://localhost", bool uv = true)
        {
            var parameters = _key.ExportParameters(false);
            var cose = new CborWriter();
            cose.WriteStartMap(5);
            cose.WriteInt32(1);
            cose.WriteInt32(2);
            cose.WriteInt32(3);
            cose.WriteInt32(-7);
            cose.WriteInt32(-1);
            cose.WriteInt32(1);
            cose.WriteInt32(-2);
            cose.WriteByteString(parameters.Q.X!);
            cose.WriteInt32(-3);
            cose.WriteByteString(parameters.Q.Y!);
            cose.WriteEndMap();
            var authData = SHA256.HashData(Encoding.UTF8.GetBytes("localhost"))
                .Concat(new byte[] { (byte)(uv ? 0x45 : 0x41), 0, 0, 0, 0 }).Concat(new byte[16])
                .Concat(new byte[] { 0, (byte)_id.Length }).Concat(_id).Concat(cose.Encode()).ToArray();
            var attestation = new CborWriter();
            attestation.WriteStartMap(3);
            attestation.WriteTextString("fmt");
            attestation.WriteTextString("none");
            attestation.WriteTextString("attStmt");
            attestation.WriteStartMap(0);
            attestation.WriteEndMap();
            attestation.WriteTextString("authData");
            attestation.WriteByteString(authData);
            attestation.WriteEndMap();
            return new { id = B64(_id), rawId = B64(_id), type = "public-key", extensions = new { }, response = new
            {
                clientDataJSON = B64(ClientData(options, "webauthn.create", origin)), attestationObject = B64(attestation.Encode()), transports = new[] { "internal" }
            } };
        }

        /// <summary>签署 assertion，可定向生成无效协议数据以验证拒绝路径。</summary>
        public object Assert(JsonElement options, Guid userId, string origin = "http://localhost", bool uv = true, bool corrupt = false)
        {
            var clientData = ClientData(options, "webauthn.get", origin);
            var authData = SHA256.HashData(Encoding.UTF8.GetBytes("localhost")).Concat(new byte[] { (byte)(uv ? 5 : 1), 0, 0, 0, 0 }).ToArray();
            var signature = _key.SignData(authData.Concat(SHA256.HashData(clientData)).ToArray(), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            if (corrupt)
            {
                signature[^1] ^= 1;
            }
            return new { id = B64(_id), rawId = B64(_id), type = "public-key", extensions = new { }, response = new
            {
                clientDataJSON = B64(clientData), authenticatorData = B64(authData), signature = B64(signature), userHandle = B64(userId.ToByteArray())
            } };
        }
        public void Dispose() => _key.Dispose();
    }
}

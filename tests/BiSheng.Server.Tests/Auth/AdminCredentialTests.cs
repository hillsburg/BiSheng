using BiSheng.Server.Auth;
using BiSheng.Server.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;

namespace BiSheng.Server.Tests.Auth;

public class AdminCredentialTests
{
    [Fact]
    public async Task TotpReplayIsRejectedAcrossScopesAndAuthVersionChanges()
    {
        using var factory = new BiShengWebAppFactory();
        var (_, id, _) = await factory.SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BiSheng.Server.Data.AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == id);
        var secret = BiSheng.Server.Services.TotpHelper.GenerateSecret();
        user.TotpSecret = scope.ServiceProvider.GetRequiredService<TotpSecretProtector>().Protect(secret);
        await db.SaveChangesAsync();
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();
        Assert.True(await scope.ServiceProvider.GetRequiredService<AdminCredentialService>().VerifyTotpAsync(user, code));
        using var second = factory.Services.CreateScope();
        var secondDb = second.ServiceProvider.GetRequiredService<BiSheng.Server.Data.AppDbContext>();
        var secondUser = await secondDb.Users.SingleAsync(u => u.Id == id);
        Assert.False(await second.ServiceProvider.GetRequiredService<AdminCredentialService>().VerifyTotpAsync(secondUser, code));
        await secondDb.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.AuthVersion, 2));
        Assert.False(await scope.ServiceProvider.GetRequiredService<AdminCredentialService>().IsAvailableAsync(user));
    }

    [Fact]
    public async Task SensitiveAuthorizationRequiresTotpAfterRecentVerificationExpires()
    {
        using var factory = new BiShengWebAppFactory();
        var (_, id, _) = await factory.SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BiSheng.Server.Data.AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == id);
        user.PasswordHash = PasswordCredentialService.HashPassword("Password1!");
        var secret = BiSheng.Server.Services.TotpHelper.GenerateSecret();
        user.TotpSecret = scope.ServiceProvider.GetRequiredService<TotpSecretProtector>().Protect(secret);
        await db.SaveChangesAsync();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        var old = DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeSeconds().ToString();
        context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim(AdminSessionService.VerifiedAtClaim, old)
        }, "Cookies"));
        var credentials = scope.ServiceProvider.GetRequiredService<AdminCredentialService>();
        Assert.False(await credentials.AuthorizeSensitiveOperationAsync(context, user, "Password1!", "", false));
        Assert.True(await credentials.AuthorizeSensitiveOperationAsync(context, user, "Password1!", new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(), false));
        // Cookie renewal cannot change the signed, fixed verification timestamp.
        Assert.False(AdminSessionService.HasRecentVerification(context.User));
    }

    [Fact]
    public async Task AccountLockoutPersistsAcrossScopesAndExpires()
    {
        using var factory = new BiShengWebAppFactory();
        var (_, id, _) = await factory.SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BiSheng.Server.Data.AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == id);
        user.PasswordHash = PasswordCredentialService.HashPassword("Password1!");
        await db.SaveChangesAsync();
        var credentials = scope.ServiceProvider.GetRequiredService<AdminCredentialService>();
        for (var i = 0; i < 10; i++) Assert.False(await credentials.VerifyPasswordAsync(user, "wrong"));
        using var second = factory.Services.CreateScope();
        var other = second.ServiceProvider.GetRequiredService<AdminCredentialService>();
        Assert.False(await other.VerifyPasswordAsync(user, "Password1!"));
        await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.AuthLockedUntilUtc, DateTime.UtcNow.AddSeconds(-1)));
        Assert.True(await other.VerifyPasswordAsync(user, "Password1!"));
    }
}

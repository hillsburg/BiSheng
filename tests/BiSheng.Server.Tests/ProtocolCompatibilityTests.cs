using System.Net.Http.Json;
using System.Text.Json;
using BiSheng.Server.Services;
using BiSheng.Server.Tests.Fixtures;
using BiSheng.Shared.Compatibility;
using Microsoft.Extensions.DependencyInjection;

namespace BiSheng.Server.Tests;

/// <summary>协议版本兼容与 verify-key 扩展字段</summary>
public class ProtocolCompatibilityTests
{
    [Theory]
    [InlineData("0.1.0", "0.1.1", -1)]
    [InlineData("0.1.1", "0.1.1", 0)]
    [InlineData("v1.0.0", "0.9.9", 1)]
    public void VersionComparer_OrdersSemVer(string left, string right, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(VersionComparer.Compare(left, right)));
    }

    [Fact]
    public async Task VerifyKey_ReportsIncompatible_WhenClientBelowMinClient()
    {
        await using var factory = new BiShengWebAppFactory(services =>
        {
            services.PostConfigure<CompatibilityOptions>(o => o.MinClient = "9.9.9");
        });
        var seed = await factory.SeedAsync();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", seed.plaintextApiKey);
        client.DefaultRequestHeaders.Add(ProtocolCompatibility.ClientVersionHeaderName, "0.1.0");

        var response = await client.GetAsync("/api/auth/verify-key");
        Assert.True(response.IsSuccessStatusCode);

        var body = await response.Content.ReadFromJsonAsync<VerifyKeyResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(body);
        Assert.True(body!.Valid);
        Assert.False(body.Compatible);
        Assert.Equal("9.9.9", body.MinClient);
        Assert.Equal("0.1.0", body.ClientVersion);
        Assert.Contains("客户端版本过旧", body.CompatibilityMessage);
    }

    [Fact]
    public async Task VerifyKey_Compatible_WhenClientMeetsMinClient()
    {
        await using var factory = new BiShengWebAppFactory();
        var seed = await factory.SeedAsync();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", seed.plaintextApiKey);
        client.DefaultRequestHeaders.Add(ProtocolCompatibility.ClientVersionHeaderName, "0.1.1");

        var response = await client.GetAsync("/api/auth/verify-key");
        var body = await response.Content.ReadFromJsonAsync<VerifyKeyResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(body);
        Assert.True(body!.Valid);
        Assert.True(body.Compatible);
        Assert.False(string.IsNullOrWhiteSpace(body.ServerVersion));
    }
}

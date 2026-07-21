using BiSheng.Server.Services;
using BiSheng.Server.Tests.Fixtures;

namespace BiSheng.Server.Tests;

/// <summary>/health 与版本比较单元/集成测试</summary>
public class HealthAndUpdateTests
{
    [Fact]
    public async Task Health_ReturnsOk_WhenDatabaseReady()
    {
        await using var factory = new BiShengWebAppFactory();
        await factory.SeedAsync();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", json);
        Assert.Contains("\"database\":\"ok\"", json);
    }

    [Theory]
    [InlineData("0.1.0", "0.1.1", -1)]
    [InlineData("0.1.1", "0.1.1", 0)]
    [InlineData("0.2.0", "0.1.9", 1)]
    [InlineData("v0.1.0", "0.1.1", -1)]
    public void CompareVersions_OrdersSemVer(string current, string latest, int expectedSign)
    {
        var cmp = ServerUpdateCheckService.CompareVersions(current, latest);
        Assert.Equal(expectedSign, Math.Sign(cmp));
    }

    [Fact]
    public void NormalizeVersion_StripsVPrefix()
    {
        Assert.Equal("1.2.3", ServerUpdateCheckService.NormalizeVersion("v1.2.3"));
        Assert.Equal("1.2.3", ServerUpdateCheckService.NormalizeVersion("1.2.3"));
    }
}

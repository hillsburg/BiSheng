using System.Net;
using System.Text;
using BiSheng.Server.Services;
using BiSheng.Server.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

    [Fact]
    public async Task CheckAsync_PrefersManifest_OverGitHub()
    {
        var handler = new StubHandler(_ =>
        {
            var json = """
                {
                  "schemaVersion": 1,
                  "server": {
                    "version": "9.9.9",
                    "rid": "linux-x64",
                    "packageFile": "BiSheng.Server-9.9.9-linux-x64.zip",
                    "downloadUrl": "https://example.com/pkg.zip",
                    "releaseNotesUrl": "https://example.com/notes"
                  }
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var options = Options.Create(new ServerUpdateOptions
        {
            Enabled = true,
            ManifestUrl = "https://example.com/update-manifest.json",
            AllowGitHubFallback = true,
            ServerRuntime = "linux-x64"
        });
        var svc = new ServerUpdateCheckService(
            new HttpClient(handler),
            options,
            NullLogger<ServerUpdateCheckService>.Instance);

        var result = await svc.CheckAsync();

        Assert.Equal(ServerUpdateAvailability.UpdateAvailable, result.Availability);
        Assert.Equal("9.9.9", result.LatestVersion);
        Assert.Equal("https://example.com/pkg.zip", result.DownloadUrl);
        Assert.Contains("清单", result.Message);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task CheckAsync_FailsCleanly_WhenNoSourceConfigured()
    {
        var options = Options.Create(new ServerUpdateOptions
        {
            Enabled = true,
            ManifestUrl = "",
            AllowGitHubFallback = false
        });
        var svc = new ServerUpdateCheckService(
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            options,
            NullLogger<ServerUpdateCheckService>.Instance);

        var result = await svc.CheckAsync();

        Assert.Equal(ServerUpdateAvailability.Failed, result.Availability);
        Assert.Contains("ManifestUrl", result.Message);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public int CallCount { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_respond(request));
        }
    }
}

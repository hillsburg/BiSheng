using System.Net;
using System.Net.Http.Headers;
using BiSheng.Server.Tests.Fixtures;

namespace BiSheng.Server.Tests.Images;

/// <summary>PR5：上传接口拒绝伪装扩展名</summary>
public class ImagesUploadE2ETests : IAsyncLifetime
{
    private BiShengWebAppFactory _factory = null!;
    private HttpClient _client = null!;

    /// <summary>初始化测试宿主与 API Key</summary>
    public async Task InitializeAsync()
    {
        _factory = new BiShengWebAppFactory();
        _client = _factory.CreateClient();
        var (apiKey, _, _) = await _factory.SeedAsync();
        _client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    }

    /// <summary>释放资源</summary>
    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>假 PNG（文本内容 + .png 扩展名）上传返回 400</summary>
    [Fact]
    public async Task Upload_FakePng_ReturnsBadRequest()
    {
        var id = Guid.NewGuid();
        using var content = new MultipartFormDataContent();
        var bytes = "this is not a png file content!!"u8.ToArray();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "evil.png");

        var resp = await _client.PostAsync($"/api/images/{id}", content);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    /// <summary>合法最小 PNG 上传成功</summary>
    [Fact]
    public async Task Upload_ValidPng_Succeeds()
    {
        var id = Guid.NewGuid();
        var resp = await UploadMinimalPngAsync(id);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    /// <summary>软删后再上传同 UUID：可下载（非假成功）</summary>
    [Fact]
    public async Task Upload_AfterSoftDelete_RevivesAndDownloadable()
    {
        var id = Guid.NewGuid();
        var upload1 = await UploadMinimalPngAsync(id);
        Assert.Equal(HttpStatusCode.OK, upload1.StatusCode);

        var deleteResp = await _client.DeleteAsync($"/api/images/{id}");
        Assert.Equal(HttpStatusCode.OK, deleteResp.StatusCode);

        var gone = await _client.GetAsync($"/api/images/{id}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);

        var upload2 = await UploadMinimalPngAsync(id);
        Assert.Equal(HttpStatusCode.OK, upload2.StatusCode);

        var download = await _client.GetAsync($"/api/images/{id}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        var bytes = await download.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);
    }

    /// <summary>上传最小合法 PNG</summary>
    private async Task<HttpResponseMessage> UploadMinimalPngAsync(Guid id)
    {
        // 1x1 透明 PNG
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(png);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "ok.png");

        return await _client.PostAsync($"/api/images/{id}", content);
    }
}

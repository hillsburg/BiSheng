using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

namespace BiSheng.Latte.Services;

/// <summary>
/// HTTP 客户端封装：自动附加 X-Api-Key Header
/// </summary>
public class ApiClient : IDisposable
{
    private HttpClient? _http;
    private readonly AuthService _authService;
    private int _disposeState;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ApiClient(AuthService authService)
    {
        _authService = authService;
    }

    private HttpClient GetHttp()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            throw new ObjectDisposedException(nameof(ApiClient));
        }

        if (_http == null || _http.BaseAddress?.ToString().TrimEnd('/') != _authService.ServerUrl?.TrimEnd('/'))
        {
            _http?.Dispose();
            _http = new HttpClient
            {
                BaseAddress = new Uri(_authService.ServerUrl!),
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        return _http;
    }

    public bool CanUseApi => _authService.IsConnected;

    public async Task<T?> GetAsync<T>(string url)
    {
        EnsureApiKey();

        try
        {
            var response = await GetHttp().GetAsync(url);
            return await ReadJsonAsync<T>(response);
        }
        catch (ApiClientException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw WrapTransportError(ex);
        }
    }

    public async Task<T?> PostAsync<T>(string url, object body)
    {
        EnsureApiKey();

        try
        {
            var response = await GetHttp().PostAsJsonAsync(url, body, JsonOptions);
            return await ReadJsonAsync<T>(response);
        }
        catch (ApiClientException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw WrapTransportError(ex);
        }
    }

    public async Task<T?> PutAsync<T>(string url, object body)
    {
        EnsureApiKey();

        try
        {
            var response = await GetHttp().PutAsJsonAsync(url, body, JsonOptions);
            return await ReadJsonAsync<T>(response);
        }
        catch (ApiClientException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw WrapTransportError(ex);
        }
    }

    public async Task DeleteAsync(string url)
    {
        EnsureApiKey();

        try
        {
            var response = await GetHttp().DeleteAsync(url);
            await EnsureSuccessOrThrowAsync(response);
        }
        catch (ApiClientException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw WrapTransportError(ex);
        }
    }

    /// <summary>
    /// GET 请求并返回原始字节数组（用于下载图片等二进制文件）
    /// </summary>
    public async Task<byte[]?> GetBytesAsync(string url)
    {
        EnsureApiKey();

        try
        {
            var response = await GetHttp().GetAsync(url);
            await EnsureSuccessOrThrowAsync(response);
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (ApiClientException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw WrapTransportError(ex);
        }
    }

    public async Task<string?> PostUploadAsync(string url, byte[] fileBytes, string fileName)
    {
        EnsureApiKey();

        try
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(fileContent, "file", fileName);
            var response = await GetHttp().PostAsync(url, content);
            var result = await ReadJsonAsync<JsonElement>(response);
            return result.TryGetProperty("url", out var u) ? u.GetString() : null;
        }
        catch (ApiClientException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw WrapTransportError(ex);
        }
    }

    private void EnsureApiKey()
    {
        var http = GetHttp();
        http.DefaultRequestHeaders.Remove("X-Api-Key");
        if (!string.IsNullOrEmpty(_authService.ApiKey))
        {
            http.DefaultRequestHeaders.Add("X-Api-Key", _authService.ApiKey);
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

        if (!response.IsSuccessStatusCode)
        {
            throw CreateHttpError(response.StatusCode, body, mediaType);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        if (!LooksLikeJson(body, mediaType))
        {
            throw new ApiClientException(
                "服务器返回了网页或非 JSON 内容，请检查服务器地址是否正确（应指向 BiSheng Server 根地址）。");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ApiClientException(
                "无法解析服务器响应，请确认服务器地址与 API Key 是否正确。",
                ex);
        }
    }

    private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        throw CreateHttpError(response.StatusCode, body, mediaType);
    }

    private static ApiClientException CreateHttpError(HttpStatusCode statusCode, string body, string mediaType)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new ApiClientException("认证失败，请检查 API Key 是否正确。");
        }

        if (statusCode == HttpStatusCode.NotFound)
        {
            return new ApiClientException("接口不存在，请检查服务器地址是否正确。");
        }

        if (LooksLikeHtml(body, mediaType))
        {
            return new ApiClientException(
                "服务器返回了网页而非 API 数据，请检查服务器地址是否正确（应指向 BiSheng Server 根地址）。");
        }

        return new ApiClientException($"服务器返回错误 ({(int)statusCode} {statusCode})。");
    }

    private static ApiClientException WrapTransportError(Exception ex)
    {
        if (ex is ApiClientException)
        {
            return (ApiClientException)ex;
        }

        if (ex is TaskCanceledException)
        {
            return new ApiClientException("连接服务器超时，请检查服务器地址与网络。", ex);
        }

        if (ex is HttpRequestException httpEx)
        {
            if (httpEx.InnerException is SocketException)
            {
                return new ApiClientException("无法连接服务器，请检查地址、端口与网络。", httpEx);
            }

            return new ApiClientException("无法访问服务器，请检查连接配置。", httpEx);
        }

        if (ex is JsonException)
        {
            return new ApiClientException(
                "无法解析服务器响应，请确认服务器地址与 API Key 是否正确。",
                ex);
        }

        return new ApiClientException(ex.Message, ex);
    }

    private static bool LooksLikeJson(string body, string mediaType)
    {
        if (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var trimmed = body.TrimStart();
        if (trimmed.Length == 0)
        {
            return true;
        }

        return trimmed[0] is '{' or '[' or '"' or '-' or '+' or '0' or '1' or '2' or '3' or '4' or '5' or '6' or '7' or '8' or '9';
    }

    private static bool LooksLikeHtml(string body, string mediaType)
    {
        if (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return body.TrimStart().StartsWith('<');
    }

    /// <summary>释放底层 HTTP 连接资源</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _http, null)?.Dispose();
    }
}

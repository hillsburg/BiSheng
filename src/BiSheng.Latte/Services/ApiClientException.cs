using System.Net.Http;

namespace BiSheng.Latte.Services;

/// <summary>API 调用失败（连接、认证或响应格式异常）</summary>
public sealed class ApiClientException : Exception
{
    public ApiClientException(string message)
        : base(message)
    {
    }

    public ApiClientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>将异常转换为用户可理解的提示（避免直接暴露 JSON 解析细节）</summary>
    public static string GetUserMessage(Exception ex)
    {
        if (ex is ApiClientException api)
        {
            return api.Message;
        }

        if (ex is System.Text.Json.JsonException)
        {
            return "无法解析服务器响应，请确认服务器地址与 API Key 是否正确。";
        }

        if (ex is HttpRequestException http)
        {
            return http.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                    => "认证失败，请检查 API Key 是否正确。",
                System.Net.HttpStatusCode.NotFound
                    => "接口不存在，请检查服务器地址是否正确。",
                _ => "无法访问服务器，请检查连接配置与网络。"
            };
        }

        if (ex is TaskCanceledException)
        {
            return "连接服务器超时，请检查服务器地址与网络。";
        }

        return ex.Message;
    }
}

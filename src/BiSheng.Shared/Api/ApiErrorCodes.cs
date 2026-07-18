namespace BiSheng.Shared.Api;

/// <summary>API 错误码常量（ProblemDetails extensions["code"]）</summary>
public static class ApiErrorCodes
{
    /// <summary>ProblemDetails type URI 前缀</summary>
    public const string TypeBase = "https://bisheng.local/errors";

    /// <summary>请求体验证失败</summary>
    public const string ValidationFailed = "validation.failed";

    /// <summary>缺少 API Key</summary>
    public const string AuthMissingKey = "auth.missing_key";

    /// <summary>API Key 无效或已停用</summary>
    public const string AuthInvalidKey = "auth.invalid_key";

    /// <summary>资源不存在</summary>
    public const string NotFound = "resource.not_found";

    /// <summary>业务请求无效</summary>
    public const string BadRequest = "request.invalid";

    /// <summary>服务端内部错误</summary>
    public const string InternalError = "internal.error";

    /// <summary>请求被限流</summary>
    public const string RateLimitExceeded = "rate_limit.exceeded";
}

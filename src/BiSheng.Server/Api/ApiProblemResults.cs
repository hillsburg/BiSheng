using BiSheng.Server.Services.Mutations;
using BiSheng.Shared.Api;
using Microsoft.AspNetCore.Mvc;

namespace BiSheng.Server.Api;

/// <summary>统一 RFC 7807 ProblemDetails 响应工厂</summary>
public static class ApiProblemResults
{
    /// <summary>将 MutationOutcome 映射为 ProblemDetails ActionResult</summary>
    public static ActionResult FromMutation(
        MutationOutcome outcome,
        string? detail,
        HttpContext httpContext)
    {
        return outcome switch
        {
            MutationOutcome.NotFound => NotFound(detail ?? "资源不存在", httpContext),
            MutationOutcome.BadRequest => BadRequest(detail ?? "请求无效", httpContext),
            MutationOutcome.InternalError => InternalError(detail, httpContext),
            _ => InternalError(null, httpContext)
        };
    }

    /// <summary>404</summary>
    public static ObjectResult NotFound(string detail, HttpContext httpContext) =>
        Problem(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "资源不存在", detail, httpContext);

    /// <summary>400</summary>
    public static ObjectResult BadRequest(string detail, HttpContext httpContext) =>
        Problem(StatusCodes.Status400BadRequest, ApiErrorCodes.BadRequest, "请求无效", detail, httpContext);

    /// <summary>401</summary>
    public static ObjectResult Unauthorized(string code, string detail, HttpContext httpContext) =>
        Problem(StatusCodes.Status401Unauthorized, code, "未授权", detail, httpContext);

    /// <summary>429</summary>
    public static ObjectResult RateLimited(HttpContext httpContext) =>
        Problem(
            StatusCodes.Status429TooManyRequests,
            ApiErrorCodes.RateLimitExceeded,
            "请求过于频繁",
            "请稍后再试",
            httpContext);

    /// <summary>500</summary>
    public static ObjectResult InternalError(string? detail, HttpContext httpContext) =>
        Problem(
            StatusCodes.Status500InternalServerError,
            ApiErrorCodes.InternalError,
            "服务器内部错误",
            detail ?? "请稍后重试",
            httpContext);

    /// <summary>构建 ProblemDetails</summary>
    public static ObjectResult Problem(
        int status,
        string code,
        string title,
        string? detail,
        HttpContext httpContext)
    {
        var problem = new ProblemDetails
        {
            Type = $"{ApiErrorCodes.TypeBase}/{code.Replace('.', '/')}",
            Title = title,
            Status = status,
            Detail = detail,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        return new ObjectResult(problem) { StatusCode = status };
    }
}

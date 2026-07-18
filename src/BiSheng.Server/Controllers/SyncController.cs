using BiSheng.Server.Auth;
using BiSheng.Server.Services;
using BiSheng.Shared.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiSheng.Server.Controllers;

/// <summary>
/// 同步 HTTP 适配层：鉴权后委托 <see cref="ISyncService"/> 执行业务逻辑
/// </summary>
[Authorize(AuthenticationSchemes = ApiKeyAuthHandler.SchemeName)]
[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private readonly ISyncService _syncService;

    /// <summary>构造同步控制器</summary>
    public SyncController(ISyncService syncService)
    {
        _syncService = syncService;
    }

    /// <summary>当前认证用户 ID</summary>
    private Guid UserId => User.GetUserId();

    /// <summary>当前 API Key ID（设备标识）</summary>
    private Guid ApiKeyId => User.GetApiKeyId();

    /// <summary>
    /// 获取服务端当前最新版本号（轻量级接口，用于客户端感知版本差距）
    /// </summary>
    [HttpGet("version")]
    public async Task<ActionResult<long>> GetServerVersion(CancellationToken ct)
    {
        var serverVersion = await _syncService.GetServerVersionAsync(UserId, ApiKeyId, ct);
        return Ok(serverVersion);
    }

    /// <summary>
    /// 拉取 since 版本之后的变更（终态折叠 + 分页）。
    /// since=0 时为实体快照；snapshotOffset 仅快照分页使用。
    /// </summary>
    [HttpGet("pull")]
    public async Task<ActionResult<SyncPullResponse>> Pull(
        [FromQuery] long since = 0,
        [FromQuery] int limit = SyncService.DefaultPullLimit,
        [FromQuery] long snapshotOffset = 0,
        CancellationToken ct = default)
    {
        var response = await _syncService.PullAsync(UserId, ApiKeyId, since, limit, snapshotOffset, ct);
        return Ok(response);
    }

    /// <summary>
    /// 客户端推送本地变更到服务端
    /// </summary>
    [HttpPost("push")]
    public async Task<ActionResult<SyncPushResponse>> Push(
        [FromBody] SyncPushRequest request,
        CancellationToken ct)
    {
        var response = await _syncService.PushAsync(UserId, ApiKeyId, request, ct);

        // 事务级失败返回 500，部分实体失败仍 200 并携带 Errors
        if (response.TransactionRolledBack)
        {
            return StatusCode(500, response);
        }

        return Ok(response);
    }
}

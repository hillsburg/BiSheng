using BiSheng.Server.Auth;
using BiSheng.Shared.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BiSheng.Server.Hubs;

/// <summary>
/// 实时同步 Hub：客户端连接后加入以 UserId 标识的 Group，
/// 服务端有变更时广播给同一用户的所有在线客户端
/// </summary>
[Authorize(AuthenticationSchemes = ApiKeyAuthHandler.SchemeName)]
public class SyncHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, userId);
        }
        await base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Hub 通知扩展方法
/// </summary>
public static class SyncHubExtensions
{
    /// <summary>
    /// 向指定用户推送变更通知
    /// </summary>
    public static async Task NotifyUserChange(
        this IHubContext<SyncHub> hubContext,
        string userId,
        ChangeDto change)
    {
        await hubContext.Clients.Group(userId).SendAsync("OnChange", change);
    }
}

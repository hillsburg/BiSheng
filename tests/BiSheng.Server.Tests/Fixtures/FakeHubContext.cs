using BiSheng.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace BiSheng.Server.Tests.Fixtures;

/// <summary>
/// 空 IHubContext&lt;SyncHub&gt;：NotifyUserChange 调用即吞，不引入第三方 mock 库
/// </summary>
internal sealed class FakeHubContext : IHubContext<SyncHub>
{
    /// <summary>Hub 客户端代理（空实现）</summary>
    public IHubClients Clients { get; } = new FakeHubClients();

    /// <summary>分组管理（空实现，测试不使用）</summary>
    public IGroupManager Groups { get; } = new FakeGroupManager();

    private sealed class FakeHubClients : IHubClients
    {
        public IClientProxy this[string connectionId] => new FakeProxy();

        public IClientProxy this[IReadOnlyList<string> connectionIds] => new FakeProxy();

        public IClientProxy All => new FakeProxy();

        public IClientProxy AllExcept(IReadOnlyList<string> excludedIds) => new FakeProxy();

        public IClientProxy Client(string connectionId) => new FakeProxy();

        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new FakeProxy();

        public IClientProxy Group(string groupName) => new FakeProxy();

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedIds) => new FakeProxy();

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => new FakeProxy();

        public IClientProxy User(string userId) => new FakeProxy();

        public IClientProxy Users(IReadOnlyList<string> userIds) => new FakeProxy();
    }

    /// <summary>空 IGroupManager：Add/Remove 直接返回</summary>
    private sealed class FakeGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>空 IClientProxy：SendCoreAsync 直接返回</summary>
    private sealed class FakeProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

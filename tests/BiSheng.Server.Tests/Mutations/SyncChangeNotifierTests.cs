using BiSheng.Server.Hubs;
using BiSheng.Server.Services.Mutations;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using Microsoft.AspNetCore.SignalR;

namespace BiSheng.Server.Tests.Mutations;

/// <summary>PR3：SignalR 通知仅含元数据、不带 Payload</summary>
public class SyncChangeNotifierTests
{
    /// <summary>主变更与级联 Delete 的推送均无 Payload</summary>
    [Fact]
    public async Task NotifyAppliedAsync_SendsMetadataOnly_WithoutPayload()
    {
        var hub = new CapturingHubContext();
        var notifier = new SyncChangeNotifier(hub);
        var noteId = Guid.NewGuid();
        var childFolderId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var applied = new AppliedMutation
        {
            Mutation = new EntityMutation
            {
                EntityType = EntityTypes.Note,
                EntityId = noteId,
                Action = ChangeActions.Update,
                Payload = """{"title":"huge","content":"should-not-be-pushed"}""",
                UpdatedAt = DateTime.UtcNow
            },
            Version = 42,
            Payload = """{"title":"huge","content":"should-not-be-pushed"}""",
            Cascaded = new[]
            {
                new CascadeAppliedMutation(EntityTypes.Folder, childFolderId, 43, DateTime.UtcNow)
            }
        };

        await notifier.NotifyAppliedAsync(userId, applied);

        Assert.Equal(2, hub.Sent.Count);

        var main = hub.Sent[0];
        Assert.Equal(EntityTypes.Note, main.EntityType);
        Assert.Equal(noteId, main.EntityId);
        Assert.Equal(ChangeActions.Update, main.Action);
        Assert.Equal(42, main.Version);
        Assert.Null(main.Payload);

        var cascaded = hub.Sent[1];
        Assert.Equal(EntityTypes.Folder, cascaded.EntityType);
        Assert.Equal(childFolderId, cascaded.EntityId);
        Assert.Equal(ChangeActions.Delete, cascaded.Action);
        Assert.Null(cascaded.Payload);
    }

    /// <summary>捕获 SendAsync("OnChange", ChangeDto) 的测试用 HubContext</summary>
    private sealed class CapturingHubContext : IHubContext<SyncHub>
    {
        /// <summary>已推送的 ChangeDto 列表</summary>
        public List<ChangeDto> Sent { get; } = new();

        /// <inheritdoc />
        public IHubClients Clients { get; }

        /// <inheritdoc />
        public IGroupManager Groups { get; } = new NoopGroups();

        /// <summary>构造捕获型 HubContext</summary>
        public CapturingHubContext()
        {
            Clients = new CapturingClients(Sent);
        }

        private sealed class CapturingClients : IHubClients
        {
            private readonly CapturingProxy _proxy;

            public CapturingClients(List<ChangeDto> sent)
            {
                _proxy = new CapturingProxy(sent);
            }

            public IClientProxy this[string connectionId] => _proxy;

            public IClientProxy this[IReadOnlyList<string> connectionIds] => _proxy;

            public IClientProxy All => _proxy;

            public IClientProxy AllExcept(IReadOnlyList<string> excludedIds) => _proxy;

            public IClientProxy Client(string connectionId) => _proxy;

            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _proxy;

            public IClientProxy Group(string groupName) => _proxy;

            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedIds) => _proxy;

            public IClientProxy Groups(IReadOnlyList<string> groupNames) => _proxy;

            public IClientProxy User(string userId) => _proxy;

            public IClientProxy Users(IReadOnlyList<string> userIds) => _proxy;
        }

        private sealed class CapturingProxy : IClientProxy
        {
            private readonly List<ChangeDto> _sent;

            public CapturingProxy(List<ChangeDto> sent) => _sent = sent;

            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                if (method == "OnChange" && args.Length > 0 && args[0] is ChangeDto dto)
                {
                    _sent.Add(dto);
                }

                return Task.CompletedTask;
            }
        }

        private sealed class NoopGroups : IGroupManager
        {
            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
        }
    }
}

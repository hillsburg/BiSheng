using BiSheng.Server.Data;
using BiSheng.Server.Services;
using BiSheng.Server.Services.Mutations;
using BiSheng.Server.Tests.Fixtures;
using BiSheng.Server.Tests.Mutations;
using Microsoft.Extensions.Logging.Abstractions;

namespace BiSheng.Server.Tests.Sync;

/// <summary>测试辅助：构造 SyncService 及其依赖</summary>
internal static class SyncServiceFactory
{
    /// <summary>用真实依赖 + FakeHubContext 构造 SyncService</summary>
    public static SyncService New(AppDbContext db) => new(
        db,
        new ClientSyncStateService(),
        new UserSyncVersionService(),
        EntityChangeWriterFactory.New(),
        new SyncChangeNotifier(new FakeHubContext()),
        NullLogger<SyncService>.Instance);
}

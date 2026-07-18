using BiSheng.Server.Data;
using BiSheng.Server.Services;
using BiSheng.Server.Services.Mutations;
using BiSheng.Server.Tests.Fixtures;
using BiSheng.Server.Tests.Sync;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using Microsoft.Extensions.Logging.Abstractions;

namespace BiSheng.Server.Tests.Mutations;

/// <summary>测试辅助：构造 MutationService</summary>
internal static class MutationServiceFactory
{
    /// <summary>构造 NoteMutationService（Writer + Notifier 与 SyncServiceFactory 一致）</summary>
    public static NoteMutationService NewNoteService(AppDbContext db) => new(
        db,
        EntityChangeWriterFactory.New(),
        new SyncChangeNotifier(new FakeHubContext()),
        NullLogger<NoteMutationService>.Instance);

    /// <summary>构造 FolderMutationService</summary>
    public static FolderMutationService NewFolderService(AppDbContext db) => new(
        db,
        EntityChangeWriterFactory.New(),
        new SyncChangeNotifier(new FakeHubContext()),
        NullLogger<FolderMutationService>.Instance);
}

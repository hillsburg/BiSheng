using BiSheng.Server.Data.Entities;
using BiSheng.Server.Services.Images;
using BiSheng.Server.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BiSheng.Server.Tests.Images;

/// <summary>PR5：孤儿图片软删 + 过期硬删</summary>
public class ImageCleanupServiceTests
{
    /// <summary>超期无引用图片会被软删除；仍被笔记引用的保留</summary>
    [Fact]
    public async Task RunAsync_SoftDeletesOrphans_KeepsReferenced()
    {
        using var fixture = new TestDbFactory();
        var referencedId = Guid.NewGuid();
        var orphanId = Guid.NewGuid();
        var (userId, _, _, _) = fixture.SeedUserWithNote(
            $"![img](bisheng://img/{referencedId})");

        fixture.Db.Images.AddRange(
            CreateImage(userId, referencedId, daysAgo: 30),
            CreateImage(userId, orphanId, daysAgo: 30));
        await fixture.Db.SaveChangesAsync();

        var service = CreateService(fixture, orphanGraceDays: 7, dryRun: false);
        var result = await service.RunAsync();

        Assert.Equal(1, result.OrphansSoftDeleted);

        var orphan = await fixture.Db.Images.FindAsync(orphanId);
        var referenced = await fixture.Db.Images.FindAsync(referencedId);
        Assert.NotNull(orphan);
        Assert.True(orphan!.IsDeleted);
        Assert.NotNull(orphan.DeletedAt);
        Assert.NotNull(referenced);
        Assert.False(referenced!.IsDeleted);
    }

    /// <summary>宽限期内的无引用图片不清理（给笔记推送留窗口）</summary>
    [Fact]
    public async Task RunAsync_RespectsOrphanGracePeriod()
    {
        using var fixture = new TestDbFactory();
        var orphanId = Guid.NewGuid();
        var (userId, _, _, _) = fixture.SeedUserWithNote("no images");

        fixture.Db.Images.Add(CreateImage(userId, orphanId, daysAgo: 2));
        await fixture.Db.SaveChangesAsync();

        var service = CreateService(fixture, orphanGraceDays: 7, dryRun: false);
        var result = await service.RunAsync();

        Assert.Equal(0, result.OrphansSoftDeleted);
        var image = await fixture.Db.Images.FindAsync(orphanId);
        Assert.False(image!.IsDeleted);
    }

    /// <summary>dry-run 只计数不写库</summary>
    [Fact]
    public async Task RunAsync_OrphanDryRun_DoesNotPersist()
    {
        using var fixture = new TestDbFactory();
        var orphanId = Guid.NewGuid();
        var (userId, _, _, _) = fixture.SeedUserWithNote("x");

        fixture.Db.Images.Add(CreateImage(userId, orphanId, daysAgo: 30));
        await fixture.Db.SaveChangesAsync();

        var service = CreateService(fixture, orphanGraceDays: 7, dryRun: true);
        var result = await service.RunAsync();

        Assert.Equal(1, result.OrphansSoftDeleted);
        var image = await fixture.Db.Images.FindAsync(orphanId);
        Assert.False(image!.IsDeleted);
    }

    /// <summary>过期软删除记录硬删出库</summary>
    [Fact]
    public async Task RunAsync_HardDeletesExpiredSoftDeleted()
    {
        using var fixture = new TestDbFactory();
        var imageId = Guid.NewGuid();
        var (userId, _, _, _) = fixture.SeedUserWithNote("x");

        fixture.Db.ServerConfigs.Add(new ServerConfig
        {
            Id = 1,
            IsSetup = true,
            ImageRetentionDays = 7,
            MaxImageSizeMb = 10
        });
        fixture.Db.Images.Add(new ServerImage
        {
            Id = imageId,
            UserId = userId,
            FileName = "gone.png",
            ContentType = "image/png",
            FileSize = 10,
            Extension = ".png",
            CreatedAt = DateTime.UtcNow.AddDays(-40),
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow.AddDays(-10)
        });
        await fixture.Db.SaveChangesAsync();

        var service = CreateService(fixture, orphanGraceDays: 7, dryRun: false);
        var result = await service.RunAsync();

        Assert.Equal(1, result.ExpiredRecordsRemoved);
        Assert.Null(await fixture.Db.Images.FindAsync(imageId));
    }

    /// <summary>构造带 Temp 内容根的清理服务</summary>
    private static ImageCleanupService CreateService(
        TestDbFactory fixture,
        int orphanGraceDays,
        bool dryRun)
    {
        var options = Options.Create(new ImageCleanupOptions
        {
            OrphanGraceDays = orphanGraceDays,
            OrphanDryRun = dryRun
        });

        return new ImageCleanupService(
            fixture.Db,
            new TestWebHostEnvironment(),
            options,
            NullLogger<ImageCleanupService>.Instance);
    }

    /// <summary>构造测试用 ServerImage</summary>
    private static ServerImage CreateImage(Guid userId, Guid id, int daysAgo)
    {
        return new ServerImage
        {
            Id = id,
            UserId = userId,
            FileName = $"{id}.png",
            ContentType = "image/png",
            FileSize = 100,
            Extension = ".png",
            CreatedAt = DateTime.UtcNow.AddDays(-daysAgo),
            IsDeleted = false
        };
    }

    /// <summary>最小化 IWebHostEnvironment，仅提供临时 ContentRootPath</summary>
    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        /// <summary>临时内容根，避免真实 uploads 副作用</summary>
        public string ContentRootPath { get; set; } = Path.GetTempPath();

        /// <summary>未使用</summary>
        public string ApplicationName { get; set; } = "test";

        /// <summary>未使用</summary>
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        /// <summary>未使用</summary>
        public string EnvironmentName { get; set; } = "Test";

        /// <summary>未使用</summary>
        public string WebRootPath { get; set; } = Path.GetTempPath();

        /// <summary>未使用</summary>
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}

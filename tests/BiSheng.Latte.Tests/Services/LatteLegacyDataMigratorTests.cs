using System.IO;
using BiSheng.Latte.Services;

namespace BiSheng.Latte.Tests.Services;

/// <summary>exe ????? LocalAppData ?????</summary>
public sealed class LatteLegacyDataMigratorTests : IDisposable
{
    private readonly string _legacyRoot;
    private readonly string _appDataRoot;

    public LatteLegacyDataMigratorTests()
    {
        _legacyRoot = Path.Combine(Path.GetTempPath(), "bisheng-legacy-" + Guid.NewGuid().ToString("N"));
        _appDataRoot = Path.Combine(Path.GetTempPath(), "bisheng-appdata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_legacyRoot);
        Directory.CreateDirectory(_appDataRoot);
        LatteAppPaths.RootOverrideForTests = _appDataRoot;
    }

    public void Dispose()
    {
        LatteAppPaths.RootOverrideForTests = null;
        TryDelete(_legacyRoot);
        TryDelete(_appDataRoot);
    }

    /// <summary>?? local.db?config.json?images ??????????</summary>
    [Fact]
    public void MigrateIfNeeded_CopiesLegacyDataAndRenamesSources()
    {
        File.WriteAllText(Path.Combine(_legacyRoot, "local.db"), "db-bytes");
        File.WriteAllText(Path.Combine(_legacyRoot, "config.json"), "{\"ServerUrl\":\"https://x\"}");
        var images = Path.Combine(_legacyRoot, "images");
        Directory.CreateDirectory(images);
        File.WriteAllBytes(Path.Combine(images, "a.png"), [1, 2, 3]);

        var result = LatteLegacyDataMigrator.MigrateIfNeeded(_legacyRoot);

        Assert.True(result.HadWork);
        Assert.Equal("db-bytes", File.ReadAllText(LatteAppPaths.DatabaseFile));
        Assert.Contains("https://x", File.ReadAllText(LatteAppPaths.ConfigFile));
        Assert.True(File.Exists(Path.Combine(LatteAppPaths.ImagesDirectory, "a.png")));
        Assert.True(File.Exists(Path.Combine(_legacyRoot, "local.db.bak-before-appdata")));
        Assert.True(File.Exists(Path.Combine(_legacyRoot, "config.json.bak-before-appdata")));
        Assert.True(Directory.Exists(images + ".bak-before-appdata"));
        Assert.False(File.Exists(Path.Combine(_legacyRoot, "local.db")));
    }

    /// <summary>???? local.db ??????????</summary>
    [Fact]
    public void MigrateIfNeeded_SkipsDatabaseWhenDestinationExists()
    {
        var legacyDb = Path.Combine(_legacyRoot, "local.db");
        File.WriteAllText(legacyDb, "old");
        File.WriteAllText(LatteAppPaths.DatabaseFile, "new");

        var result = LatteLegacyDataMigrator.MigrateIfNeeded(_legacyRoot);

        Assert.False(result.HadWork);
        Assert.Equal("new", File.ReadAllText(LatteAppPaths.DatabaseFile));
        Assert.True(File.Exists(legacyDb));
        Assert.Contains("local.db", result.Summary, StringComparison.Ordinal);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // ??????
        }
    }
}

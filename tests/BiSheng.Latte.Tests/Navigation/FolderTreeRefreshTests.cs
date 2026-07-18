using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services;
using BiSheng.Latte.Tests.Fixtures;
using BiSheng.Latte.ViewModels;
using Xunit;

namespace BiSheng.Latte.Tests.Navigation;

/// <summary>FolderTreeViewModel.Refresh：同 Id 不替换 SelectedFolder 引用</summary>
[Collection("WpfSta")]
public class FolderTreeRefreshTests : IDisposable
{
    private readonly LatteTestDbFactory _fixture;

    public FolderTreeRefreshTests()
    {
        _fixture = new LatteTestDbFactory();
    }

    public void Dispose() => _fixture.Dispose();

    /// <summary>Refresh 后 selectedId 不变 → SelectedFolder 引用保持不变</summary>
    [StaFact]
    public void Refresh_SameSelectedId_KeepsSelectedFolderReference()
    {
        var folderId = Guid.NewGuid();
        _fixture.Db.Folders.Add(new LocalFolder { Id = folderId, Name = "Before" });
        _fixture.Db.SaveChanges();

        var tree = new FolderTreeViewModel(
            new LocalChangeTracker(() => new LocalDbContext()),
            () => new LocalDbContext(),
            NavigationTestPublisher.Create().Publisher,
            NavigationTestPublisher.CreateFilterState());
        tree.Refresh();
        tree.SelectedFolder = _fixture.Db.Folders.Find(folderId);
        var referenceBefore = tree.SelectedFolder;

        _fixture.Db.Folders.Find(folderId)!.Name = "After";
        _fixture.Db.SaveChanges();

        tree.Refresh();

        Assert.Same(referenceBefore, tree.SelectedFolder);
        Assert.Equal(folderId, tree.SelectedFolder!.Id);
    }
}

using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services.Search;
using BiSheng.Latte.Tests.Fixtures;
using Xunit;

namespace BiSheng.Latte.Tests.Search;

/// <summary>NoteContentSearchService：本地全文搜索</summary>
public class NoteContentSearchServiceTests : IDisposable
{
    private readonly LatteTestDbFactory _fixture;

    public NoteContentSearchServiceTests()
    {
        _fixture = new LatteTestDbFactory();
    }

    public void Dispose() => _fixture.Dispose();

    /// <summary>正文关键词应命中</summary>
    [Fact]
    public async Task SearchAsync_ContentMatch_ReturnsNote()
    {
        var folderId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        _fixture.Db.Folders.Add(new LocalFolder { Id = folderId, Name = "工作" });
        _fixture.Db.Notes.Add(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "周报",
            Content = "本周完成了 **alpha** 功能"
        });
        _fixture.Db.SaveChanges();

        var service = new NoteContentSearchService(() => new LocalDbContext());
        var results = await service.SearchAsync("alpha", titleOnly: false);

        Assert.Single(results);
        Assert.Equal(noteId, results[0].NoteId);
        Assert.Contains("工作", results[0].FolderPath);
    }

    /// <summary>仅标题模式不搜正文</summary>
    [Fact]
    public async Task SearchAsync_TitleOnly_SkipsContent()
    {
        var folderId = Guid.NewGuid();
        _fixture.Db.Folders.Add(new LocalFolder { Id = folderId, Name = "F" });
        _fixture.Db.Notes.Add(new LocalNote
        {
            Id = Guid.NewGuid(),
            FolderId = folderId,
            Title = "可见标题",
            Content = "隐藏关键词"
        });
        _fixture.Db.SaveChanges();

        var service = new NoteContentSearchService(() => new LocalDbContext());
        var results = await service.SearchAsync("隐藏关键词", titleOnly: true);

        Assert.Empty(results);
    }
}

namespace BiSheng.Latte.Services.Search;

/// <summary>笔记正文全文搜索</summary>
public interface INoteContentSearchService
{
    /// <summary>全库搜索</summary>
    Task<IReadOnlyList<NoteSearchResultItem>> SearchAsync(
        string query,
        bool titleOnly,
        CancellationToken cancellationToken = default);

    /// <summary>单篇笔记内命中列表</summary>
    Task<IReadOnlyList<NoteSearchHitItem>> GetHitsAsync(
        Guid noteId,
        string query,
        bool titleOnly,
        CancellationToken cancellationToken = default);

    /// <summary>加载笔记 Markdown 正文（预览用）</summary>
    Task<string?> GetNoteContentAsync(Guid noteId, CancellationToken cancellationToken = default);
}

namespace BiSheng.Latte.Services.Search;

/// <summary>全文搜索：单篇笔记结果项</summary>
public sealed class NoteSearchResultItem
{
    /// <summary>笔记 Id</summary>
    public Guid NoteId { get; init; }

    /// <summary>所属文件夹 Id</summary>
    public Guid FolderId { get; init; }

    /// <summary>笔记标题</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>文件夹路径展示（如「工作 / 项目」）</summary>
    public string FolderPath { get; init; } = string.Empty;

    /// <summary>命中次数（标题计 1 + 正文多处）</summary>
    public int HitCount { get; init; }
}

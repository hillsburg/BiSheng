namespace BiSheng.Latte.Services.Search;

/// <summary>单篇笔记内一处关键词命中</summary>
public sealed class NoteSearchHitItem
{
    /// <summary>命中序号（0-based，供上/下切换）</summary>
    public int Index { get; init; }

    /// <summary>PlainText 坐标起点</summary>
    public int PlainTextOffset { get; init; }

    /// <summary>命中长度</summary>
    public int Length { get; init; }

    /// <summary>上下文摘要</summary>
    public string Snippet { get; init; } = string.Empty;

    /// <summary>是否命中标题（坐标基于标题而非正文 PlainText）</summary>
    public bool IsTitleHit { get; init; }

    /// <summary>Markdown 原文中的 Caret 起点（预览/编辑器定位用）</summary>
    public int MarkdownCaretOffset { get; init; }

    /// <summary>Markdown 原文中的选区长度</summary>
    public int MarkdownSelectionLength { get; init; }
}

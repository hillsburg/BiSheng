namespace BiSheng.Latte.Models;

/// <summary>从全文搜索弹窗跳转主编辑器的一次性定位意图</summary>
public sealed class EditorNavigationIntent
{
    /// <summary>目标笔记 Id</summary>
    public Guid NoteId { get; init; }

    /// <summary>目标文件夹 Id</summary>
    public Guid FolderId { get; init; }

    /// <summary>基于 PlainText 的命中起点</summary>
    public int PlainTextOffset { get; init; }

    /// <summary>命中长度（PlainText 坐标）</summary>
    public int MatchLength { get; init; }

    /// <summary>是否命中标题（坐标基于标题而非正文 PlainText）</summary>
    public bool IsTitleHit { get; init; }

    /// <summary>Markdown 原文 Caret（优先于 PlainText 映射）</summary>
    public int MarkdownCaretOffset { get; init; }

    /// <summary>Markdown 原文选区长度</summary>
    public int MarkdownSelectionLength { get; init; }

    /// <summary>搜索关键词（fallback 定位用）</summary>
    public string Query { get; init; } = string.Empty;
}

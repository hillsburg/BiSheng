namespace BiSheng.Latte.Services.Search;

/// <summary>Markdown 提取为 PlainText 及坐标映射</summary>
public sealed class NoteSearchExtraction
{
    /// <summary>可搜索纯文本</summary>
    public string PlainText { get; init; } = string.Empty;

    /// <summary>plainText[i] 对应 markdown 中的起始下标</summary>
    public int[] PlainToMarkdown { get; init; } = Array.Empty<int>();
}

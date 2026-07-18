namespace BiSheng.Latte.Services.Search;

/// <summary>PlainText 命中坐标映射到 Markdown 编辑器落点</summary>
public static class NoteSearchNavigation
{
    /// <summary>编辑器落点</summary>
    public sealed record CaretPosition(int CaretOffset, int SelectionLength);

    /// <summary>将 PlainText 命中映射为 Markdown CaretOffset</summary>
    public static CaretPosition MapToCaretOffset(
        string markdown,
        int plainTextOffset,
        int matchLength,
        string query,
        bool isTitleHit)
    {
        if (isTitleHit || string.IsNullOrEmpty(markdown))
        {
            return FindQueryInMarkdown(markdown, query, occurrenceIndex: 0);
        }

        return MapToCaretOffset(markdown, plainTextOffset, matchLength, query);
    }

    /// <summary>基于已提取结果映射，避免重复 Extract</summary>
    public static CaretPosition MapFromExtraction(
        NoteSearchExtraction extraction,
        int plainTextOffset,
        int matchLength)
    {
        if (extraction.PlainToMarkdown.Length == 0
            || plainTextOffset < 0
            || plainTextOffset >= extraction.PlainToMarkdown.Length)
        {
            return new CaretPosition(0, 0);
        }

        var caret = extraction.PlainToMarkdown[plainTextOffset];
        var endPlain = Math.Min(plainTextOffset + matchLength - 1, extraction.PlainToMarkdown.Length - 1);
        var endMarkdown = extraction.PlainToMarkdown[endPlain] + 1;
        return new CaretPosition(caret, Math.Max(1, endMarkdown - caret));
    }

    /// <summary>将 PlainText 命中映射为 Markdown CaretOffset</summary>
    public static CaretPosition MapToCaretOffset(string markdown, int plainTextOffset, int matchLength, string query)
    {
        var extraction = NoteSearchTextExtractor.Extract(markdown);
        var mapped = MapFromExtraction(extraction, plainTextOffset, matchLength);
        if (mapped.SelectionLength > 0)
        {
            return mapped;
        }

        return FindQueryInMarkdown(markdown, query, occurrenceIndex: 0);
    }

    /// <summary>在 Markdown 中查找第 n 次出现的 query（0-based）</summary>
    public static CaretPosition FindQueryInMarkdown(string markdown, string query, int occurrenceIndex)
    {
        if (string.IsNullOrEmpty(markdown) || string.IsNullOrWhiteSpace(query))
        {
            return new CaretPosition(0, 0);
        }

        var q = query.Trim();
        var start = 0;
        var found = 0;
        while (start <= markdown.Length - q.Length)
        {
            var idx = markdown.IndexOf(q, start, StringComparison.CurrentCultureIgnoreCase);
            if (idx < 0)
            {
                break;
            }

            if (found == occurrenceIndex)
            {
                return new CaretPosition(idx, q.Length);
            }

            found++;
            start = idx + Math.Max(1, q.Length);
        }

        return new CaretPosition(0, 0);
    }
}

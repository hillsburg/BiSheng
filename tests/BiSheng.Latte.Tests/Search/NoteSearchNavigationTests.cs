using BiSheng.Latte.Services.Search;
using Xunit;

namespace BiSheng.Latte.Tests.Search;

/// <summary>NoteSearchNavigation：多处命中映射</summary>
public class NoteSearchNavigationTests
{
    /// <summary>同一关键词多处命中应映射到不同 Markdown 坐标</summary>
    [Fact]
    public void MapFromExtraction_MultipleHits_ReturnsDistinctOffsets()
    {
        const string markdown = "alpha 中间内容 alpha 结尾 alpha";
        var extraction = NoteSearchTextExtractor.Extract(markdown);
        var hits = NoteSearchTextExtractor.FindAll(extraction.PlainText, "alpha");

        Assert.True(hits.Count >= 3);

        var positions = hits
            .Select(h => NoteSearchNavigation.MapFromExtraction(extraction, h.Offset, h.Length).CaretOffset)
            .Distinct()
            .ToList();

        Assert.Equal(3, positions.Count);
    }

    /// <summary>FindQueryInMarkdown 支持第 n 次出现</summary>
    [Fact]
    public void FindQueryInMarkdown_NthOccurrence_ReturnsCorrectOffset()
    {
        const string markdown = "foo bar foo baz foo";

        var first = NoteSearchNavigation.FindQueryInMarkdown(markdown, "foo", 0);
        var second = NoteSearchNavigation.FindQueryInMarkdown(markdown, "foo", 1);
        var third = NoteSearchNavigation.FindQueryInMarkdown(markdown, "foo", 2);

        Assert.Equal(0, first.CaretOffset);
        Assert.Equal(8, second.CaretOffset);
        Assert.Equal(16, third.CaretOffset);
    }
}

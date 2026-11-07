using BiSheng.Editor.Model;
using BiSheng.Editor.Parsing;

namespace BiSheng.Latte.Tests.Editor;

/// <summary>
/// 单行 Markdown 分析：行类型、前缀长度、行内标记偏移。
/// 渲染层直接用这些偏移隐藏语法符号、定位样式，偏移错一位就会露出符号或吞掉正文。
/// </summary>
public sealed class LineMarkdownAnalyzerTests
{
    // ===== 行类型 =====

    [Theory]
    [InlineData("# 标题", LineType.Heading1)]
    [InlineData("###### 六级", LineType.Heading6)]
    [InlineData("  ## 缩进标题", LineType.Heading2)]
    [InlineData("#没有空格", LineType.Normal)]
    [InlineData("####### 七个井号", LineType.Normal)]
    [InlineData("- 列表", LineType.BulletList)]
    [InlineData("* 列表", LineType.BulletList)]
    [InlineData("+ 列表", LineType.BulletList)]
    [InlineData("12. 序号", LineType.OrderedList)]
    [InlineData("> 引用", LineType.BlockQuote)]
    [InlineData(">引用", LineType.BlockQuote)]
    [InlineData("```csharp", LineType.CodeBlock)]
    [InlineData("---", LineType.HorizontalRule)]
    [InlineData("***", LineType.HorizontalRule)]
    [InlineData("___", LineType.HorizontalRule)]
    [InlineData("   ", LineType.EmptyLine)]
    [InlineData("普通文字", LineType.Normal)]
    [InlineData("-没有空格", LineType.Normal)]
    public void AnalyzeLineType_ClassifiesBlockSyntax(string line, LineType expected)
    {
        Assert.Equal(expected, LineMarkdownAnalyzer.AnalyzeLineType(line));
    }

    // ===== 前缀长度（标题折叠、列表标记绘制的定位依据） =====

    [Theory]
    [InlineData("# 标题", 2)]
    [InlineData("### 标题", 4)]
    [InlineData("  # 缩进", 4)]
    [InlineData("#   多个空格", 4)]
    [InlineData("普通", 0)]
    public void GetHeadingPrefixLength_IncludesIndentAndSpaces(string line, int expected)
    {
        Assert.Equal(expected, LineMarkdownAnalyzer.GetHeadingPrefixLength(line));
    }

    [Theory]
    [InlineData("- 项", 0, 2, null)]
    [InlineData("    - 嵌套", 4, 6, null)]
    [InlineData("3. 序号", 0, 3, "3")]
    [InlineData("  10. 序号", 2, 6, "10")]
    public void TryGetListPrefixInfo_ReturnsIndentPrefixAndNumber(
        string line, int indent, int prefix, string? number)
    {
        Assert.True(LineMarkdownAnalyzer.TryGetListPrefixInfo(line, out var info));
        Assert.Equal(indent, info.IndentLength);
        Assert.Equal(prefix, info.PrefixLength);
        Assert.Equal(number, info.OrderedNumber);
    }

    [Fact]
    public void TryGetListPrefixInfo_PlainLine_ReturnsFalse()
    {
        Assert.False(LineMarkdownAnalyzer.TryGetListPrefixInfo("普通文字", out _));
        Assert.Equal(0, LineMarkdownAnalyzer.GetListPrefixLength("普通文字"));
    }

    // ===== 行内标记：单一语法的偏移 =====

    [Fact]
    public void Bold_OffsetsCoverDelimitersAndContent()
    {
        var marker = Single(LineMarkdownAnalyzer.AnalyzeInlineElements("前**粗体**后"), InlineStyle.Bold);

        AssertMarker(marker, syntaxStart: 1, start: 3, end: 4, syntaxEnd: 6);
    }

    [Fact]
    public void UnderscoreBold_IsRecognized()
    {
        var marker = Single(LineMarkdownAnalyzer.AnalyzeInlineElements("__粗__"), InlineStyle.Bold);

        AssertMarker(marker, syntaxStart: 0, start: 2, end: 2, syntaxEnd: 4);
    }

    [Fact]
    public void Italic_OffsetsCoverDelimitersAndContent()
    {
        var marker = Single(LineMarkdownAnalyzer.AnalyzeInlineElements("a *斜体* b"), InlineStyle.Italic);

        AssertMarker(marker, syntaxStart: 2, start: 3, end: 4, syntaxEnd: 5);
    }

    [Fact]
    public void InlineCode_OffsetsCoverBackticksAndContent()
    {
        var marker = Single(LineMarkdownAnalyzer.AnalyzeInlineElements("用 `code` 吧"), InlineStyle.InlineCode);

        AssertMarker(marker, syntaxStart: 2, start: 3, end: 6, syntaxEnd: 7);
    }

    [Fact]
    public void Link_ContentIsTextOnly_SyntaxIncludesUrl()
    {
        var marker = Single(
            LineMarkdownAnalyzer.AnalyzeInlineElements("见[文档](https://x.com)。"),
            InlineStyle.Link);

        // 渲染层隐藏 [ 与 ](url)，只露出「文档」
        AssertMarker(marker, syntaxStart: 1, start: 2, end: 3, syntaxEnd: 19);
    }

    [Fact]
    public void Image_IsNotAlsoTreatedAsLink()
    {
        var markers = LineMarkdownAnalyzer.AnalyzeInlineElements("![图](a.png)");

        Assert.Single(markers);
        Assert.Equal(InlineStyle.Image, markers[0].Style);
    }

    [Theory]
    [InlineData("2 * 3 * 4")]
    [InlineData("a * b")]
    [InlineData("\\*不是斜体\\*")]
    public void SpacedOrEscapedAsterisks_AreNotItalic(string line)
    {
        Assert.DoesNotContain(
            LineMarkdownAnalyzer.AnalyzeInlineElements(line),
            m => m.Style == InlineStyle.Italic);
    }

    // ===== 行内代码内不解析其他语法 =====

    [Theory]
    [InlineData("`**不粗**`")]
    [InlineData("`[不是链接](x)`")]
    [InlineData("`*不斜*`")]
    public void SyntaxInsideInlineCode_IsLiteral(string line)
    {
        var markers = LineMarkdownAnalyzer.AnalyzeInlineElements(line);

        Assert.Single(markers);
        Assert.Equal(InlineStyle.InlineCode, markers[0].Style);
    }

    // ===== 嵌套与边界 =====

    [Fact]
    public void LinkInsideBold_IsStillALink()
    {
        var markers = LineMarkdownAnalyzer.AnalyzeInlineElements("**[文档](https://x.com)**");

        Assert.Contains(markers, m => m.Style == InlineStyle.Bold);
        var link = Single(markers, InlineStyle.Link);
        AssertMarker(link, syntaxStart: 2, start: 3, end: 4, syntaxEnd: 20);
    }

    [Fact]
    public void ItalicInsideBold_IsStillItalic()
    {
        var markers = LineMarkdownAnalyzer.AnalyzeInlineElements("**粗 *斜* 粗**");

        Assert.Contains(markers, m => m.Style == InlineStyle.Bold);
        var italic = Single(markers, InlineStyle.Italic);
        AssertMarker(italic, syntaxStart: 4, start: 5, end: 5, syntaxEnd: 6);
    }

    [Fact]
    public void BoldInsideLinkText_KeepsBoth()
    {
        var markers = LineMarkdownAnalyzer.AnalyzeInlineElements("[**粗**](u)");

        Assert.Contains(markers, m => m.Style == InlineStyle.Bold);
        Assert.Contains(markers, m => m.Style == InlineStyle.Link);
    }

    [Theory]
    [InlineData("snake_case_name")]
    [InlineData("my_file_v2.md")]
    public void IntrawordUnderscores_AreNotItalic(string line)
    {
        Assert.DoesNotContain(
            LineMarkdownAnalyzer.AnalyzeInlineElements(line),
            m => m.Style == InlineStyle.Italic);
    }

    [Fact]
    public void EmptyBold_HasNoContent()
    {
        var marker = Single(LineMarkdownAnalyzer.AnalyzeInlineElements("****"), InlineStyle.Bold);

        // 渲染层据此判断「中间无内容」而不隐藏符号，便于继续输入
        Assert.True(marker.StartIndex > marker.EndIndex);
    }

    [Fact]
    public void MultipleMarkers_AreAllFound()
    {
        var markers = LineMarkdownAnalyzer.AnalyzeInlineElements("**a** 和 *b* 和 `c` 和 [d](e)");

        Assert.Equal(
            new[] { InlineStyle.Bold, InlineStyle.InlineCode, InlineStyle.Italic, InlineStyle.Link }.OrderBy(s => s),
            markers.Select(m => m.Style).OrderBy(s => s));
    }

    [Fact]
    public void TripleAsterisk_IsItalicWrappingBold()
    {
        var markers = LineMarkdownAnalyzer.AnalyzeInlineElements("***x***");

        // 外层斜体占一个 *，内层粗体占两个 *，渲染时叠加为粗斜体，且不残留字面量星号
        AssertMarker(Single(markers, InlineStyle.Italic), 0, 1, 5, 6);
        AssertMarker(Single(markers, InlineStyle.Bold), 1, 3, 3, 5);
    }

    [Fact]
    public void TripleUnderscore_IsItalicWrappingBold()
    {
        var markers = LineMarkdownAnalyzer.AnalyzeInlineElements("___粗斜___");

        AssertMarker(Single(markers, InlineStyle.Italic), 0, 1, 6, 7);
        AssertMarker(Single(markers, InlineStyle.Bold), 1, 3, 4, 6);
    }

    [Fact]
    public void TripleAsterisk_InSentence_ProducesExactlyTwoMarkers()
    {
        var markers = LineMarkdownAnalyzer.AnalyzeInlineElements("前 ***重点*** 后");

        Assert.Equal(2, markers.Count);
        AssertMarker(Single(markers, InlineStyle.Italic), 2, 3, 8, 9);
        AssertMarker(Single(markers, InlineStyle.Bold), 3, 5, 6, 8);
    }

    [Fact]
    public void LinkUrl_WithBalancedParentheses_IsNotTruncated()
    {
        const string line = "[C](https://en.wikipedia.org/wiki/C_(language))";
        var link = Single(LineMarkdownAnalyzer.AnalyzeInlineElements(line), InlineStyle.Link);

        AssertMarker(link, 0, 1, 1, line.Length - 1);
    }

    [Fact]
    public void LinkFollowedByParenthesizedText_StopsAtOwnClosingParen()
    {
        var link = Single(LineMarkdownAnalyzer.AnalyzeInlineElements("[a](x) (备注)"), InlineStyle.Link);

        AssertMarker(link, 0, 1, 1, 5);
    }

    [Fact]
    public void ImageUrl_WithBalancedParentheses_IsNotTruncated()
    {
        const string line = "![图](a_(1).png)";
        var image = Single(LineMarkdownAnalyzer.AnalyzeInlineElements(line), InlineStyle.Image);

        Assert.Equal(line.Length - 1, image.SyntaxEndIndex);
    }

    [Theory]
    [InlineData("> 引用", 2)]
    [InlineData(">引用", 1)]
    [InlineData("  > 缩进引用", 4)]
    [InlineData(">  两个空格", 2)]
    [InlineData("普通", 0)]
    public void GetBlockQuotePrefixLength_IncludesIndent(string line, int expected)
    {
        Assert.Equal(expected, LineMarkdownAnalyzer.GetBlockQuotePrefixLength(line));
    }

    [Fact]
    public void IndentedQuote_TypeAndPrefixAgree()
    {
        const string line = "  > 缩进引用";

        // 行类型判为引用时，折叠长度必须覆盖缩进与 >，否则会出现“认作引用却显示原始符号”
        Assert.Equal(LineType.BlockQuote, LineMarkdownAnalyzer.AnalyzeLineType(line));
        Assert.Equal(4, LineMarkdownAnalyzer.GetBlockQuotePrefixLength(line));
    }

    private static InlineMarker Single(List<InlineMarker> markers, InlineStyle style)
    {
        var matches = markers.Where(m => m.Style == style).ToList();
        Assert.True(matches.Count == 1, $"期望恰好一个 {style}，实际 {matches.Count} 个");
        return matches[0];
    }

    private static void AssertMarker(InlineMarker marker, int syntaxStart, int start, int end, int syntaxEnd)
    {
        Assert.Equal(
            (syntaxStart, start, end, syntaxEnd),
            (marker.SyntaxStartIndex, marker.StartIndex, marker.EndIndex, marker.SyntaxEndIndex));
    }
}

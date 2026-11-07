using BiSheng.Editor.Model;
using ICSharpCode.AvalonEdit.Document;

namespace BiSheng.Latte.Tests.Editor;

/// <summary>
/// 文档模型增量更新：任何编辑序列之后，行状态都必须与对同一文本整篇重建的结果完全一致。
/// </summary>
public sealed class MarkdownDocumentModelTests
{
    /// <summary>随机插入/删除/替换片段（含栅栏、换行、行内语法），每步都与全量重建比对</summary>
    [Fact]
    public void RandomEdits_IncrementalStateMatchesFullRebuild()
    {
        var random = new Random(20261106);
        string[] snippets =
        [
            "```", "```\n", "\n```\n", "\n", "\r\n", "# 标题", "- 列表", "1. 序号", "> 引用",
            "**粗体**", "*斜体*", "`code`", "[链接](https://example.com)", "![图](a.png)",
            "普通文字", "---", "  ", "x"
        ];

        var document = new TextDocument("# 开头\n正文 **粗** 体\n```\ncode\n```\n- a\n- b\n");
        var model = new MarkdownDocumentModel(document);

        for (int step = 0; step < 600; step++)
        {
            int offset = random.Next(document.TextLength + 1);
            int removal = random.Next(3) == 0
                ? random.Next(Math.Min(12, document.TextLength - offset) + 1)
                : 0;
            string insertion = random.Next(4) == 0 ? string.Empty : snippets[random.Next(snippets.Length)];

            if (removal == 0 && insertion.Length == 0)
            {
                continue;
            }

            document.Replace(offset, removal, insertion);
            AssertMatchesFullRebuild(model, document, step);
        }
    }

    /// <summary>输入开栅栏后，下方（编辑区之外）的行变为代码块内容，需要通知视图重绘</summary>
    [Fact]
    public void AddingFence_ReportsLinesBelowAsChanged()
    {
        var document = new TextDocument("正文\n# 标题\n- 列表\n");
        var model = new MarkdownDocumentModel(document);
        (int First, int Last)? reported = null;
        model.LineTypesChanged += (first, last) => reported = (first, last);

        // 插入后：0 ``` / 1 正文 / 2 # 标题 / 3 - 列表 / 4 空行；第 0~1 行属于编辑区，由 AvalonEdit 自行重绘
        document.Insert(0, "```\n");

        Assert.NotNull(reported);
        Assert.Equal(2, reported!.Value.First);
        Assert.Equal(4, reported.Value.Last);
        Assert.Equal(LineType.CodeBlockContent, model.GetLineState(2).Type);
    }

    /// <summary>普通行内编辑不影响其他行，不应触发额外重绘</summary>
    [Fact]
    public void EditingPlainLine_DoesNotReportOtherLines()
    {
        var document = new TextDocument("第一行\n第二行\n第三行\n");
        var model = new MarkdownDocumentModel(document);
        var reported = false;
        model.LineTypesChanged += (_, _) => reported = true;

        document.Insert(document.GetLineByNumber(2).Offset, "**加粗**");

        Assert.False(reported);
        Assert.Single(model.GetLineState(1).InlineMarkers);
    }

    private static void AssertMatchesFullRebuild(MarkdownDocumentModel model, TextDocument document, int step)
    {
        var expected = new MarkdownDocumentModel(new TextDocument(document.Text));

        for (int i = 0; i < document.LineCount; i++)
        {
            var actualLine = model.GetLineState(i);
            var expectedLine = expected.GetLineState(i);
            var where = $"step {step}, line {i}";

            Assert.True(i == actualLine.LineNumber, $"{where}: LineNumber");
            Assert.True(expectedLine.RawText == actualLine.RawText, $"{where}: RawText");
            Assert.True(expectedLine.Type == actualLine.Type, $"{where}: Type {expectedLine.Type} != {actualLine.Type}");
            Assert.True(expectedLine.BlockStartLine == actualLine.BlockStartLine, $"{where}: BlockStartLine");
            Assert.True(expectedLine.BlockEndLine == actualLine.BlockEndLine, $"{where}: BlockEndLine");
            Assert.True(
                Describe(expectedLine.InlineMarkers) == Describe(actualLine.InlineMarkers),
                $"{where}: InlineMarkers");
        }
    }

    private static string Describe(IEnumerable<InlineMarker> markers) =>
        string.Join(";", markers.Select(m =>
            $"{m.Style}:{m.SyntaxStartIndex}-{m.StartIndex}-{m.EndIndex}-{m.SyntaxEndIndex}"));
}

using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using BiSheng.Editor.Model;

namespace BiSheng.Editor.Parsing
{
    /// <summary>
    /// 基于 Markdig 的 Markdown 解析器
    /// </summary>
    public class MarkdownParser
    {
        private readonly MarkdownPipeline _pipeline;

        public MarkdownParser()
        {
            _pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();
        }

        /// <summary>
        /// 解析完整文档，返回 AST
        /// </summary>
        public MarkdownDocument Parse(string markdown)
        {
            return Markdown.Parse(markdown, _pipeline);
        }

        /// <summary>
        /// 从 AST 中提取每一行的块信息
        /// </summary>
        public Dictionary<int, LineType> GetLineTypes(string markdown)
        {
            var result = new Dictionary<int, LineType>();
            var doc = Parse(markdown);

            foreach (var block in doc)
            {
                var startLine = block.Line;
                var endLine = GetBlockLastLine(block, markdown);

                switch (block)
                {
                    case HeadingBlock heading:
                        var headingType = heading.Level switch
                        {
                            1 => LineType.Heading1,
                            2 => LineType.Heading2,
                            3 => LineType.Heading3,
                            4 => LineType.Heading4,
                            5 => LineType.Heading5,
                            6 => LineType.Heading6,
                            _ => LineType.Normal
                        };
                        result[startLine] = headingType;
                        break;

                    case QuoteBlock quote:
                        for (int i = startLine; i <= endLine; i++)
                            result[i] = LineType.BlockQuote;
                        break;

                    case FencedCodeBlock codeBlock:
                        result[startLine] = LineType.CodeBlock;
                        result[endLine] = LineType.CodeBlock;
                        for (int i = startLine + 1; i < endLine; i++)
                            result[i] = LineType.CodeBlockContent;
                        break;

                    case ListBlock listBlock:
                        foreach (var item in listBlock)
                        {
                            if (item is ListItemBlock listItem)
                            {
                                var itemStart = listItem.Line;
                                var itemEnd = GetBlockLastLine(listItem, markdown);
                                result[itemStart] = listBlock.IsOrdered
                                    ? LineType.OrderedList
                                    : LineType.BulletList;
                                for (int i = itemStart + 1; i <= itemEnd; i++)
                                {
                                    if (!result.ContainsKey(i))
                                        result[i] = LineType.Normal;
                                }
                            }
                        }
                        break;

                    case ThematicBreakBlock:
                        result[startLine] = LineType.HorizontalRule;
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// 通过 Span 计算块的最后一行号（0-based）
        /// </summary>
        private static int GetBlockLastLine(Block block, string markdown)
        {
            // Markdig 的 Span.End 是字符偏移，需要转换为行号
            int endOffset = block.Span.End;
            if (endOffset >= markdown.Length)
                endOffset = markdown.Length - 1;
            if (endOffset < 0) return block.Line;

            int line = 0;
            for (int i = 0; i <= endOffset && i < markdown.Length; i++)
            {
                if (markdown[i] == '\n') line++;
            }
            return line;
        }
    }
}

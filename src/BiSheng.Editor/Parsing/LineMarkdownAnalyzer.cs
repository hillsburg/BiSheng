using BiSheng.Editor.Model;
using System.Text.RegularExpressions;

namespace BiSheng.Editor.Parsing
{
    /// <summary>
    /// 单行 Markdown 快速分析器，用于行级渲染决策
    /// </summary>
    public static class LineMarkdownAnalyzer
    {
        // 标题: # ~ ######
        private static readonly Regex HeadingRegex = new(@"^(#{1,6})\s+", RegexOptions.Compiled);
        // 无序列表: - * + 开头
        private static readonly Regex BulletListRegex = new(@"^(\s*)([-*+])\s+", RegexOptions.Compiled);
        // 有序列表: 数字. 开头
        private static readonly Regex OrderedListRegex = new(@"^(\s*)(\d+)\.\s+", RegexOptions.Compiled);
        // 引用: > 开头
        private static readonly Regex BlockQuoteRegex = new(@"^>\s?", RegexOptions.Compiled);
        // 代码块: ``` 开头
        private static readonly Regex CodeBlockFenceRegex = new(@"^```", RegexOptions.Compiled);
        // 水平分割线: --- 或 ***
        private static readonly Regex HorizontalRuleRegex = new(@"^(-{3,}|\*{3,}|_{3,})\s*$", RegexOptions.Compiled);
        // 图片: ![alt](url)
        private static readonly Regex ImageRegex = new(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled);
        // 行内代码: `code`
        private static readonly Regex InlineCodeRegex = new(@"`([^`]+)`", RegexOptions.Compiled);
        // 加粗: **text** 或 __text__
        private static readonly Regex BoldRegex = new(@"(\*\*|__)(.*?)\1", RegexOptions.Compiled);
        // 斜体: *text* 或 _text_
        private static readonly Regex ItalicRegex = new(@"(?<![*\\])(\*|_)(?!\s)(.*?)(?<!\s)\1(?!\*)", RegexOptions.Compiled);
        // 链接: [text](url)
        private static readonly Regex LinkRegex = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);

        /// <summary>
        /// 分析单行文本的类型
        /// </summary>
        public static LineType AnalyzeLineType(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return LineType.EmptyLine;

            line = line.TrimStart();

            if (HeadingRegex.IsMatch(line))
            {
                var match = HeadingRegex.Match(line);
                int level = match.Groups[1].Length;
                return level switch
                {
                    1 => LineType.Heading1,
                    2 => LineType.Heading2,
                    3 => LineType.Heading3,
                    4 => LineType.Heading4,
                    5 => LineType.Heading5,
                    6 => LineType.Heading6,
                    _ => LineType.Normal
                };
            }

            if (CodeBlockFenceRegex.IsMatch(line))
                return LineType.CodeBlock;

            if (HorizontalRuleRegex.IsMatch(line))
                return LineType.HorizontalRule;

            if (BulletListRegex.IsMatch(line))
                return LineType.BulletList;

            if (OrderedListRegex.IsMatch(line))
                return LineType.OrderedList;

            if (BlockQuoteRegex.IsMatch(line))
                return LineType.BlockQuote;

            return LineType.Normal;
        }

        /// <summary>
        /// 分析行内 Markdown 元素
        /// </summary>
        public static List<InlineMarker> AnalyzeInlineElements(string line)
        {
            var markers = new List<InlineMarker>();
            if (string.IsNullOrEmpty(line)) return markers;

            // 行内代码
            foreach (Match match in InlineCodeRegex.Matches(line))
            {
                markers.Add(new InlineMarker
                {
                    SyntaxStartIndex = match.Index,
                    StartIndex = match.Index + 1,
                    EndIndex = match.Index + match.Length - 2,
                    SyntaxEndIndex = match.Index + match.Length - 1,
                    Style = InlineStyle.InlineCode
                });
            }

            // 图片（优先于链接检测）
            foreach (Match match in ImageRegex.Matches(line))
            {
                // 排除已在行内代码中的
                if (IsInsideExisting(markers, match.Index)) continue;
                markers.Add(new InlineMarker
                {
                    SyntaxStartIndex = match.Index,
                    StartIndex = match.Index,
                    EndIndex = match.Index + match.Length - 1,
                    SyntaxEndIndex = match.Index + match.Length - 1,
                    Style = InlineStyle.Image
                });
            }

            // 加粗
            foreach (Match match in BoldRegex.Matches(line))
            {
                if (IsInsideExisting(markers, match.Index)) continue;
                int delimLen = match.Groups[1].Length;
                markers.Add(new InlineMarker
                {
                    SyntaxStartIndex = match.Index,
                    StartIndex = match.Index + delimLen,
                    EndIndex = match.Index + match.Length - delimLen - 1,
                    SyntaxEndIndex = match.Index + match.Length - 1,
                    Style = InlineStyle.Bold
                });
            }

            // 斜体（在加粗之后）
            foreach (Match match in ItalicRegex.Matches(line))
            {
                if (IsInsideExisting(markers, match.Index)) continue;
                markers.Add(new InlineMarker
                {
                    SyntaxStartIndex = match.Index,
                    StartIndex = match.Index + 1,
                    EndIndex = match.Index + match.Length - 2,
                    SyntaxEndIndex = match.Index + match.Length - 1,
                    Style = InlineStyle.Italic
                });
            }

            // 链接
            foreach (Match match in LinkRegex.Matches(line))
            {
                if (IsInsideExisting(markers, match.Index)) continue;
                markers.Add(new InlineMarker
                {
                    SyntaxStartIndex = match.Index,
                    StartIndex = match.Index + 1,
                    EndIndex = match.Index + match.Groups[1].Length,
                    SyntaxEndIndex = match.Index + match.Length - 1,
                    Style = InlineStyle.Link
                });
            }

            return markers;
        }

        private static bool IsInsideExisting(List<InlineMarker> markers, int index)
        {
            return markers.Any(m => index >= m.SyntaxStartIndex && index <= m.SyntaxEndIndex);
        }

        /// <summary>
        /// 获取标题前缀总长度（含行首空白 + "# "…），供视觉折叠与样式定位。
        /// 不改写源文本：缩进标题在渲染时顶格显示。
        /// 例如 "  # 标题" 返回 4；无标题返回 0。
        /// </summary>
        public static int GetHeadingPrefixLength(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return 0;
            }

            var trimmed = line.TrimStart();
            var match = HeadingRegex.Match(trimmed);
            if (!match.Success)
            {
                return 0;
            }

            int indent = line.Length - trimmed.Length;
            return indent + match.Length;
        }

        /// <summary>
        /// 获取列表前缀长度
        /// </summary>
        public static int GetListPrefixLength(string line)
        {
            return TryGetListPrefixInfo(line, out var info) ? info.PrefixLength : 0;
        }

        /// <summary>
        /// 解析列表前缀：缩进、整段前缀长度、有序序号（无序为 null）
        /// </summary>
        public static bool TryGetListPrefixInfo(string line, out ListPrefixInfo info)
        {
            var bulletMatch = BulletListRegex.Match(line);
            if (bulletMatch.Success)
            {
                info = new ListPrefixInfo(
                    bulletMatch.Groups[1].Length,
                    bulletMatch.Length,
                    orderedNumber: null);
                return true;
            }

            var orderedMatch = OrderedListRegex.Match(line);
            if (orderedMatch.Success)
            {
                info = new ListPrefixInfo(
                    orderedMatch.Groups[1].Length,
                    orderedMatch.Length,
                    orderedMatch.Groups[2].Value);
                return true;
            }

            info = default;
            return false;
        }
    }

    /// <summary>列表行前缀解析结果</summary>
    public readonly struct ListPrefixInfo
    {
        /// <summary>前导空白长度</summary>
        public int IndentLength { get; }

        /// <summary>含缩进与标记在内的前缀总长度</summary>
        public int PrefixLength { get; }

        /// <summary>有序列表数字；无序为 null</summary>
        public string? OrderedNumber { get; }

        /// <summary>是否为有序列表</summary>
        public bool IsOrdered => OrderedNumber != null;

        /// <summary>构造前缀信息</summary>
        public ListPrefixInfo(int indentLength, int prefixLength, string? orderedNumber)
        {
            IndentLength = indentLength;
            PrefixLength = prefixLength;
            OrderedNumber = orderedNumber;
        }
    }
}

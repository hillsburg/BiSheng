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
        // 任务勾选框: 紧跟在列表标记之后的 [ ] / [x] / [X]，其后须为空白或行尾（GFM 任务列表）
        private static readonly Regex TaskBoxRegex = new(@"\G\[([ xX])\](?:[ \t]+|$)", RegexOptions.Compiled);
        // 引用: > 开头
        private static readonly Regex BlockQuoteRegex = new(@"^>\s?", RegexOptions.Compiled);
        // 代码块: ``` 开头
        private static readonly Regex CodeBlockFenceRegex = new(@"^```", RegexOptions.Compiled);
        // 水平分割线: --- 或 ***
        private static readonly Regex HorizontalRuleRegex = new(@"^(-{3,}|\*{3,}|_{3,})\s*$", RegexOptions.Compiled);
        // 图片: ![alt](url)，url 允许一层成对括号（同链接）
        private static readonly Regex ImageRegex = new(@"!\[([^\]]*)\]\(((?:[^()]|\([^()]*\))+)\)", RegexOptions.Compiled);
        // 行内代码: `code`
        private static readonly Regex InlineCodeRegex = new(@"`([^`]+)`", RegexOptions.Compiled);
        // 粗斜体: ***text*** 或 ___text___（须先于加粗识别，否则 ** 会吃掉三连符号中的两个）
        private static readonly Regex BoldItalicRegex = new(@"(\*{3}|_{3})(?!\s)(.+?)(?<!\s)\1", RegexOptions.Compiled);
        // 加粗: **text** 或 __text__
        private static readonly Regex BoldRegex = new(@"(\*\*|__)(.*?)\1", RegexOptions.Compiled);
        // 斜体: *text* 或 _text_
        private static readonly Regex ItalicRegex = new(@"(?<![*\\])(\*|_)(?!\s)(.*?)(?<!\s)\1(?!\*)", RegexOptions.Compiled);
        // 链接: [text](url)，url 允许一层成对括号，如 https://en.wikipedia.org/wiki/C_(language)
        private static readonly Regex LinkRegex = new(@"\[([^\]]+)\]\(((?:[^()]|\([^()]*\))+)\)", RegexOptions.Compiled);

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

            // 粗斜体：拆成外层斜体（单个符号）+ 内层粗体（两个符号），渲染时两种字形属性叠加
            AddNestable(markers, BoldItalicRegex, line, match => new InlineMarker
            {
                SyntaxStartIndex = match.Index,
                StartIndex = match.Index + 1,
                EndIndex = match.Index + match.Length - 2,
                SyntaxEndIndex = match.Index + match.Length - 1,
                Style = InlineStyle.Italic
            },
            outer => new InlineMarker
            {
                SyntaxStartIndex = outer.StartIndex,
                StartIndex = outer.StartIndex + 2,
                EndIndex = outer.EndIndex - 2,
                SyntaxEndIndex = outer.EndIndex,
                Style = InlineStyle.Bold
            });

            // 加粗
            AddNestable(markers, BoldRegex, line, match =>
            {
                int delimLen = match.Groups[1].Length;
                return new InlineMarker
                {
                    SyntaxStartIndex = match.Index,
                    StartIndex = match.Index + delimLen,
                    EndIndex = match.Index + match.Length - delimLen - 1,
                    SyntaxEndIndex = match.Index + match.Length - 1,
                    Style = InlineStyle.Bold
                };
            });

            // 斜体（在加粗之后：加粗的 ** 会先被正则当成空斜体匹配到，由重叠规则拒掉）
            AddNestable(markers, ItalicRegex, line, match => new InlineMarker
            {
                SyntaxStartIndex = match.Index,
                StartIndex = match.Index + 1,
                EndIndex = match.Index + match.Length - 2,
                SyntaxEndIndex = match.Index + match.Length - 1,
                Style = InlineStyle.Italic
            });

            // 链接
            AddNestable(markers, LinkRegex, line, match => new InlineMarker
            {
                SyntaxStartIndex = match.Index,
                StartIndex = match.Index + 1,
                EndIndex = match.Index + match.Groups[1].Length,
                SyntaxEndIndex = match.Index + match.Length - 1,
                Style = InlineStyle.Link
            });

            return markers;
        }

        /// <summary>
        /// 逐个匹配并加入可嵌套的行内标记（粗体、斜体、链接）。
        /// 某个匹配被拒后从下一个字符重新匹配，而不是跳过整段：
        /// 否则 "**粗 *斜* 粗**" 中开头的 ** 被当作空斜体吞掉后，真正的 *斜* 就再也匹配不到。
        /// </summary>
        /// <param name="buildInner">可选：外层被接受后随之加入的内层标记（用于粗斜体），不再单独做重叠检查</param>
        private static void AddNestable(
            List<InlineMarker> markers,
            Regex regex,
            string line,
            Func<Match, InlineMarker> build,
            Func<InlineMarker, InlineMarker>? buildInner = null)
        {
            int position = 0;
            while (position < line.Length)
            {
                var match = regex.Match(line, position);
                if (!match.Success)
                {
                    break;
                }

                var candidate = build(match);
                if (!IsIntrawordUnderscore(line, candidate) && CanPlace(markers, candidate))
                {
                    markers.Add(candidate);
                    if (buildInner != null)
                    {
                        markers.Add(buildInner(candidate));
                    }

                    position = match.Index + match.Length;
                }
                else
                {
                    position = match.Index + 1;
                }
            }
        }

        /// <summary>
        /// 新标记能否放入：行内代码与图片内部按字面量处理，任何重叠都拒绝；
        /// 其余标记允许完全嵌套（一方整体落在另一方的内容区内），但不允许交叉或压住对方的语法符号
        /// </summary>
        private static bool CanPlace(List<InlineMarker> markers, InlineMarker candidate)
        {
            foreach (var existing in markers)
            {
                bool disjoint = candidate.SyntaxEndIndex < existing.SyntaxStartIndex
                                || candidate.SyntaxStartIndex > existing.SyntaxEndIndex;
                if (disjoint)
                {
                    continue;
                }

                if (existing.Style is InlineStyle.InlineCode or InlineStyle.Image)
                {
                    return false;
                }

                bool candidateInsideExisting = candidate.SyntaxStartIndex >= existing.StartIndex
                                               && candidate.SyntaxEndIndex <= existing.EndIndex;
                bool existingInsideCandidate = existing.SyntaxStartIndex >= candidate.StartIndex
                                               && existing.SyntaxEndIndex <= candidate.EndIndex;
                if (!candidateInsideExisting && !existingInsideCandidate)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 下划线强调不能出现在单词中间（CommonMark 规则）：snake_case_name、my_file_v2.md 不应变成斜体
        /// </summary>
        private static bool IsIntrawordUnderscore(string line, InlineMarker candidate)
        {
            if (line[candidate.SyntaxStartIndex] != '_')
            {
                return false;
            }

            int before = candidate.SyntaxStartIndex - 1;
            int after = candidate.SyntaxEndIndex + 1;
            return (before >= 0 && char.IsLetterOrDigit(line[before]))
                   || (after < line.Length && char.IsLetterOrDigit(line[after]));
        }

        /// <summary>图片只需避开行内代码（代码里的 ![..](..) 是字面量）</summary>
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
        /// 获取引用前缀总长度（含行首空白 + "&gt;" + 至多一个空白），供视觉折叠与退格删除前缀。
        /// 与 <see cref="AnalyzeLineType"/> 的判定口径一致：缩进的引用同样识别并折叠。
        /// 例如 "  &gt; 引用" 返回 4；非引用返回 0。
        /// </summary>
        public static int GetBlockQuotePrefixLength(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return 0;
            }

            var trimmed = line.TrimStart();
            var match = BlockQuoteRegex.Match(trimmed);
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
        /// 解析列表前缀：缩进、整段前缀长度、有序序号（无序为 null）及任务勾选框。
        /// 任务项的前缀包含 "[ ] "，使隐藏、标记绘制与退格删前缀都把勾选框当作前缀的一部分。
        /// </summary>
        public static bool TryGetListPrefixInfo(string line, out ListPrefixInfo info)
        {
            var bulletMatch = BulletListRegex.Match(line);
            if (bulletMatch.Success)
            {
                info = WithTaskBox(line, bulletMatch.Groups[1].Length, bulletMatch.Length, orderedNumber: null);
                return true;
            }

            var orderedMatch = OrderedListRegex.Match(line);
            if (orderedMatch.Success)
            {
                info = WithTaskBox(line, orderedMatch.Groups[1].Length, orderedMatch.Length,
                    orderedMatch.Groups[2].Value);
                return true;
            }

            info = default;
            return false;
        }

        /// <summary>列表标记之后若紧跟勾选框，把勾选框并入前缀并记录其位置与状态</summary>
        private static ListPrefixInfo WithTaskBox(string line, int indent, int markerLength, string? orderedNumber)
        {
            var taskMatch = TaskBoxRegex.Match(line, markerLength);
            if (!taskMatch.Success)
            {
                return new ListPrefixInfo(indent, markerLength, orderedNumber);
            }

            bool isChecked = taskMatch.Groups[1].Value != " ";
            return new ListPrefixInfo(indent, markerLength + taskMatch.Length, orderedNumber,
                markerLength, isChecked);
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

        /// <summary>是否为任务项（带 [ ] / [x] 勾选框）</summary>
        public bool IsTask { get; }

        /// <summary>勾选框 '[' 在行内的偏移，仅 <see cref="IsTask"/> 为真时有效；勾选状态字符位于其后一位</summary>
        public int TaskBoxStart { get; }

        /// <summary>任务项是否已勾选</summary>
        public bool IsChecked { get; }

        /// <summary>构造普通列表前缀信息</summary>
        public ListPrefixInfo(int indentLength, int prefixLength, string? orderedNumber)
        {
            IndentLength = indentLength;
            PrefixLength = prefixLength;
            OrderedNumber = orderedNumber;
        }

        /// <summary>构造任务项前缀信息，前缀长度已包含勾选框及其后空白</summary>
        public ListPrefixInfo(int indentLength, int prefixLength, string? orderedNumber,
            int taskBoxStart, bool isChecked)
            : this(indentLength, prefixLength, orderedNumber)
        {
            IsTask = true;
            TaskBoxStart = taskBoxStart;
            IsChecked = isChecked;
        }
    }
}

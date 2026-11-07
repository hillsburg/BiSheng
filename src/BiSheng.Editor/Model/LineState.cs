namespace BiSheng.Editor.Model
{
    /// <summary>
    /// 行类型枚举
    /// </summary>
    public enum LineType
    {
        Normal,
        Heading1,
        Heading2,
        Heading3,
        Heading4,
        Heading5,
        Heading6,
        BulletList,
        OrderedList,
        BlockQuote,
        CodeBlock,
        CodeBlockContent,
        HorizontalRule,
        Image,
        EmptyLine
    }

    /// <summary>
    /// 行状态信息
    /// </summary>
    public class LineState
    {
        public int LineNumber { get; set; }
        public LineType Type { get; set; } = LineType.Normal;
        public int BlockStartLine { get; set; } = -1;
        public int BlockEndLine { get; set; } = -1;
        public string RawText { get; set; } = string.Empty;

        /// <summary>
        /// 行内元素标记：(startIndex, endIndex, InlineStyle)
        /// </summary>
        public List<InlineMarker> InlineMarkers { get; set; } = new();

        // ===== 增量更新缓存：只由行文本决定，行未编辑时跨按键复用 =====

        /// <summary>是否为 ``` 栅栏行（代码块配对的唯一依据）</summary>
        internal bool IsFence { get; set; }

        /// <summary>正则分析结果是否已缓存；位于代码块内的行在离开代码块前不做正则分析</summary>
        internal bool IsAnalyzed { get; set; }

        /// <summary>按普通行分析得到的行类型（不考虑代码块上下文）</summary>
        internal LineType AnalyzedType { get; set; } = LineType.Normal;

        /// <summary>按普通行分析得到的行内标记（不考虑代码块上下文）</summary>
        internal List<InlineMarker>? AnalyzedMarkers { get; set; }
    }

    /// <summary>
    /// 行内样式标记
    /// </summary>
    public enum InlineStyle
    {
        Bold,
        Italic,
        InlineCode,
        Link,
        Image
    }

    public class InlineMarker
    {
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public InlineStyle Style { get; set; }
        public int SyntaxStartIndex { get; set; }
        public int SyntaxEndIndex { get; set; }
    }
}

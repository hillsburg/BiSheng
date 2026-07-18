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

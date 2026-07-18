using System.Windows;
using System.Windows.Controls;
using BiSheng.Editor.Model;
using BiSheng.Editor.Parsing;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace BiSheng.Editor.Rendering
{
    /// <summary>
    /// 语法折叠器：将块级 Markdown 语法标记替换为零宽度的 VisualLineElement，
    /// 使内容文本视觉上从行首开始，格式符号不占任何空间。
    /// 处理：标题前缀（含行首缩进，视觉顶格）、引用前缀、代码围栏、水平分割线。
    /// 列表前缀由 MarkdownLineTransformer 处理（保留缩进以供 Bullet 定位）。
    /// 不改写文档源文本。
    /// </summary>
    public class SyntaxCollapser : VisualLineElementGenerator
    {
        private readonly MarkdownDocumentModel _documentModel;

        public SyntaxCollapser(MarkdownDocumentModel documentModel)
        {
            _documentModel = documentModel;
        }

        public override int GetFirstInterestedOffset(int startOffset)
        {
            var doc = CurrentContext.Document;
            var line = doc.GetLineByOffset(startOffset);

            // 仅在行首触发
            if (startOffset != line.Offset)
                return -1;

            int lineIndex = line.LineNumber - 1;
            var state = _documentModel.GetLineState(lineIndex);
            if (state == null) return -1;

            // 仅对需要零宽度折叠的块级类型感兴趣（列表除外）
            switch (state.Type)
            {
                case LineType.Heading1:
                case LineType.Heading2:
                case LineType.Heading3:
                case LineType.Heading4:
                case LineType.Heading5:
                case LineType.Heading6:
                case LineType.BlockQuote:
                case LineType.CodeBlock:
                case LineType.HorizontalRule:
                    return startOffset;

                default:
                    return -1;
            }
        }

        public override VisualLineElement? ConstructElement(int offset)
        {
            var doc = CurrentContext.Document;
            var line = doc.GetLineByOffset(offset);
            int lineIndex = line.LineNumber - 1;
            var state = _documentModel.GetLineState(lineIndex);
            if (state == null) return null;

            var text = doc.GetText(line.Offset, line.Length);
            if (string.IsNullOrEmpty(text)) return null;

            int prefixLen = GetCollapseLength(state.Type, text, line.Length);
            if (prefixLen <= 0) return null;

            // 用零宽度的 InlineObjectElement 替换语法文本
            var zeroWidthElement = new Border { Width = 0, Height = 0 };
            return new InlineObjectElement(prefixLen, zeroWidthElement);
        }

        /// <summary>
        /// 获取需要折叠的文本长度
        /// </summary>
        private static int GetCollapseLength(LineType type, string text, int lineLength)
        {
            switch (type)
            {
                case LineType.Heading1:
                case LineType.Heading2:
                case LineType.Heading3:
                case LineType.Heading4:
                case LineType.Heading5:
                case LineType.Heading6:
                    return LineMarkdownAnalyzer.GetHeadingPrefixLength(text);

                case LineType.BlockQuote:
                    return text.StartsWith("> ") ? 2 : (text.StartsWith(">") ? 1 : 0);

                case LineType.CodeBlock:
                    return lineLength; // 折叠整个 ```language 行

                case LineType.HorizontalRule:
                    return lineLength; // 折叠整个 --- 行

                default:
                    return 0;
            }
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BiSheng.Editor.Controls.MarkdownEditor.Themes;
using BiSheng.Editor.Model;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace BiSheng.Editor.Rendering
{
    /// <summary>
    /// 块级元素 VisualLineElementGenerator
    /// 用于在 AvalonEdit 中渲染代码块背景、引用左边框、水平分割线等
    /// </summary>
    public class BlockElementGenerator : VisualLineElementGenerator
    {
        private readonly MarkdownDocumentModel _documentModel;
        private readonly MarkdownTheme _theme;
        private int _currentLineNumber = -1;

        public BlockElementGenerator(MarkdownDocumentModel documentModel, MarkdownTheme theme)
        {
            _documentModel = documentModel;
            _theme = theme;
        }

        public void SetCurrentLine(int lineNumber)
        {
            _currentLineNumber = lineNumber;
        }

        public override int GetFirstInterestedOffset(int startOffset)
        {
            // 对每一行的开头感兴趣
            var doc = CurrentContext.Document;
            var line = doc.GetLineByOffset(startOffset);
            int lineIndex = line.LineNumber - 1;

            // 只在行首触发
            if (startOffset == line.Offset)
                return startOffset;

            return -1;
        }

        public override VisualLineElement ConstructElement(int offset)
        {
            // 不在这里构造元素，块级样式通过 LineTransformer 实现
            return null;
        }
    }

    /// <summary>
    /// 水平分割线渲染器
    /// </summary>
    public class HorizontalRuleRenderer : VisualLineElementGenerator
    {
        private readonly MarkdownDocumentModel _documentModel;
        private readonly MarkdownTheme _theme;
        private int _currentLineNumber = -1;

        public HorizontalRuleRenderer(MarkdownDocumentModel documentModel, MarkdownTheme theme)
        {
            _documentModel = documentModel;
            _theme = theme;
        }

        public void SetCurrentLine(int lineNumber)
        {
            _currentLineNumber = lineNumber;
        }

        public override int GetFirstInterestedOffset(int startOffset)
        {
            var doc = CurrentContext.Document;
            var line = doc.GetLineByOffset(startOffset);
            if (startOffset != line.Offset) return -1;

            int lineIndex = line.LineNumber - 1;

            var state = _documentModel.GetLineState(lineIndex);
            if (state?.Type == LineType.HorizontalRule)
                return startOffset;

            return -1;
        }

        public override VisualLineElement ConstructElement(int offset)
        {
            // 返回 null，让 LineTransformer 处理样式
            return null;
        }
    }
}

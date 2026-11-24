using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using BiSheng.Editor.Controls.MarkdownEditor.Themes;
using BiSheng.Editor.Model;
using BiSheng.Editor.Parsing;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace BiSheng.Editor.Rendering
{
    /// <summary>把任务列表前缀排成可点击的复选框，Markdown 原文记录勾选状态。</summary>
    internal sealed class TaskListRenderer : VisualLineElementGenerator
    {
        private const double CheckboxFontScale = 0.9;
        private const double CheckboxCenterAboveBaseline = 0.36;
        private const double ContentGap = 6;

        private readonly MarkdownDocumentModel _documentModel;
        private readonly TextEditor _editor;
        private readonly ControlTemplate _checkboxTemplate;

        /// <summary>主题切换时更新此引用，下一次排版会生成使用新配色的复选框。</summary>
        public MarkdownTheme Theme { get; set; }

        public TaskListRenderer(
            MarkdownDocumentModel documentModel, TextEditor editor, MarkdownTheme theme, ControlTemplate checkboxTemplate)
        {
            _documentModel = documentModel;
            _editor = editor;
            Theme = theme;
            _checkboxTemplate = checkboxTemplate;
        }

        public override int GetFirstInterestedOffset(int startOffset)
        {
            var line = CurrentContext.Document.GetLineByOffset(startOffset);
            if (!TryGetTaskPrefix(line, out _, out var prefix))
            {
                return -1;
            }

            int prefixOffset = line.Offset + prefix.IndentLength;
            return startOffset <= prefixOffset ? prefixOffset : -1;
        }

        public override VisualLineElement? ConstructElement(int offset)
        {
            var document = CurrentContext.Document;
            var line = document.GetLineByOffset(offset);
            if (!TryGetTaskPrefix(line, out var text, out var prefix)
                || offset != line.Offset + prefix.IndentLength
                || prefix.TaskStateOffset is not int stateOffset)
            {
                return null;
            }

            double fontSize = _editor.FontSize;
            double size = fontSize * CheckboxFontScale;
            double centerAboveBaseline = fontSize * CheckboxCenterAboveBaseline;
            // AvalonEdit 从行顶放置行内控件，补上顶部间距，让方框中心对齐普通正文字形。
            double top = Math.Max(0, CurrentContext.TextView.DefaultBaseline - centerAboveBaseline - size / 2);

            // 勾选前后共用同样的尺寸；模板只换描边、填充和对勾，文字光标留在原位。
            var checkbox = new CheckBox
            {
                Template = _checkboxTemplate,
                BorderBrush = Theme.BulletColor,
                Foreground = Theme.LinkColor,
                Width = size,
                Height = size,
                Margin = new Thickness(0, top, ContentGap, 0),
                Focusable = false,
                IsTabStop = false,
                IsEnabled = !_editor.IsReadOnly,
                IsChecked = text[stateOffset] is 'x' or 'X',
                Cursor = Cursors.Hand
            };
            TextBlock.SetBaselineOffset(checkbox, top + size / 2 + centerAboveBaseline);
            AutomationProperties.SetName(checkbox, "任务完成状态");
            // 复选框初始化之后才监听，鼠标和 UI Automation 都通过同一条路径修改文档。
            checkbox.Checked += (_, _) => SetTaskChecked(document, line, isChecked: true);
            checkbox.Unchecked += (_, _) => SetTaskChecked(document, line, isChecked: false);

            // 缩进保留为普通文本，只有列表符号和任务标记占用同一个视觉单元。
            return new InlineObjectElement(prefix.PrefixLength - prefix.IndentLength, checkbox);
        }

        /// <summary>只处理文档模型确认的任务行，代码块中的相同文字交给普通代码渲染。</summary>
        private bool TryGetTaskPrefix(DocumentLine line, out string text, out ListPrefixInfo prefix)
        {
            var state = _documentModel.GetLineState(line.LineNumber - 1);
            text = state.RawText;
            prefix = default;
            return state.Type == LineType.TaskList
                && LineMarkdownAnalyzer.TryGetListPrefixInfo(text, out prefix);
        }

        /// <summary>重新定位当前状态字符，使用等长替换保留光标与选区。</summary>
        private void SetTaskChecked(TextDocument document, DocumentLine line, bool isChecked)
        {
            if (_editor.IsReadOnly || !ReferenceEquals(document, _editor.Document) || line.IsDeleted
                || !TryGetTaskPrefix(line, out var text, out var prefix)
                || prefix.TaskStateOffset is not int stateOffset)
            {
                return;
            }

            if ((text[stateOffset] is 'x' or 'X') != isChecked)
            {
                document.Replace(line.Offset + stateOffset, 1, isChecked ? "x" : " ",
                    OffsetChangeMappingType.CharacterReplace);
            }
        }
    }
}

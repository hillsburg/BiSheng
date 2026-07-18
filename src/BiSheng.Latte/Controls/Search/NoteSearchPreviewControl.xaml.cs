using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;

namespace BiSheng.Latte.Controls.Search;

/// <summary>全文搜索第三列：只读 Markdown 预览与命中高亮</summary>
public partial class NoteSearchPreviewControl : UserControl
{
    /// <summary>构造预览控件</summary>
    public NoteSearchPreviewControl()
    {
        InitializeComponent();
        PreviewHost.IsReadOnly = true;
        PreviewHost.ShowLineNumbers = false;
        PreviewHost.WordWrap = true;
        PreviewHost.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei");
        PreviewHost.FontSize = 13;
        PreviewHost.Background = Brushes.Transparent;
    }

    /// <summary>设置 Markdown 正文</summary>
    public void SetContent(string markdown)
    {
        PreviewHost.Text = markdown ?? string.Empty;
        ClearHighlight();
    }

    /// <summary>按 Markdown 坐标高亮并滚动到可见区域</summary>
    public void HighlightAt(int caretOffset, int selectionLength)
    {
        ClearHighlight();
        if (selectionLength <= 0 || PreviewHost.Document.TextLength == 0)
        {
            return;
        }

        var maxOffset = PreviewHost.Document.TextLength;
        caretOffset = Math.Clamp(caretOffset, 0, maxOffset);
        selectionLength = Math.Min(selectionLength, maxOffset - caretOffset);
        if (selectionLength <= 0)
        {
            return;
        }

        PreviewHost.CaretOffset = caretOffset;
        PreviewHost.SelectionLength = selectionLength;

        var line = PreviewHost.Document.GetLineByOffset(caretOffset);
        PreviewHost.ScrollTo(line.LineNumber, 1);

        Dispatcher.BeginInvoke(() =>
        {
            PreviewHost.TextArea.Caret.BringCaretToView();
        }, DispatcherPriority.Loaded);
    }

    private void ClearHighlight()
    {
        PreviewHost.SelectionLength = 0;
    }
}

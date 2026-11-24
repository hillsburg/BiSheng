using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using BiSheng.Editor.Controls.MarkdownEditor;
using BiSheng.Editor.Controls.MarkdownEditor.Themes;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace BiSheng.Latte.Tests.Editor;

/// <summary>通过实际编辑器控件验证任务列表的渲染和 Markdown 编辑。</summary>
[Collection("WpfSta")]
public sealed class MarkdownTaskListTests
{
    /// <summary>在同一个 STA 线程验证渲染、勾选和键盘编辑，复用编辑器共享的主题资源。</summary>
    [StaFact]
    public void TaskLists_RenderAndEditMarkdown()
    {
        var control = new MarkdownEditorControl();
        control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var editor = control.TextEditor;
        VerifyToggleAndUndo(editor);
        VerifyEditingKeys(editor);
        VerifyThemeAndFontSize(control);
    }

    /// <summary>切换状态只改一个字符，保留选区并可撤销；围栏内的示例保持源码。</summary>
    private static void VerifyToggleAndUndo(TextEditor editor)
    {
        editor.Text = "- [ ] 任务\n```\n- [x] 示例\n```";
        editor.Select(6, 2);

        var textView = editor.TextArea.TextView;
        var line = editor.Document.GetLineByNumber(1);
        var visualLine = textView.GetOrConstructVisualLine(line);
        var box = Assert.IsType<CheckBox>(Assert.Single(visualLine.Elements.OfType<InlineObjectElement>()).Element);
        var toggle = Assert.IsAssignableFrom<IToggleProvider>(
            new CheckBoxAutomationPeer(box).GetPattern(PatternInterface.Toggle));
        Assert.False(box.IsChecked);

        toggle.Toggle();
        Assert.StartsWith("- [x] 任务\n", editor.Text);
        Assert.Equal(8, editor.CaretOffset);
        Assert.Equal("任务", editor.SelectedText);

        editor.Undo();
        Assert.StartsWith("- [ ] 任务\n", editor.Text);
        editor.Redo();
        Assert.StartsWith("- [x] 任务\n", editor.Text);

        var checkedLine = textView.GetOrConstructVisualLine(line);
        var checkedBox = Assert.IsType<CheckBox>(
            Assert.Single(checkedLine.Elements.OfType<InlineObjectElement>()).Element);
        Assert.Equal(visualLine.Height, checkedLine.Height);
        Assert.True(checkedBox.IsChecked);
        var uncheck = Assert.IsAssignableFrom<IToggleProvider>(
            new CheckBoxAutomationPeer(checkedBox).GetPattern(PatternInterface.Toggle));
        uncheck.Toggle();
        Assert.StartsWith("- [ ] 任务\n", editor.Text);

        var codeLine = textView.GetOrConstructVisualLine(editor.Document.GetLineByNumber(3));
        Assert.DoesNotContain(codeLine.Elements.OfType<InlineObjectElement>(), item => item.Element is CheckBox);
    }

    /// <summary>回车复用列表符号、缩进及换行符，空项退出后可用退格移除其他任务项的前缀。</summary>
    private static void VerifyEditingKeys(TextEditor editor)
    {
        using var source = new HwndSource(new HwndSourceParameters("MarkdownTaskListTests"));
        editor.Text = "列表\r\n  * [X] 任务";
        editor.CaretOffset = editor.Document.TextLength;

        PressKey(editor, source, Key.Enter);
        Assert.Equal("列表\r\n  * [X] 任务\r\n  * [ ] ", editor.Text);
        Assert.Equal(editor.Document.TextLength, editor.CaretOffset);

        PressKey(editor, source, Key.Enter);
        Assert.Equal("列表\r\n  * [X] 任务\r\n", editor.Text);

        editor.CaretOffset = "列表\r\n  * [X] ".Length;
        PressKey(editor, source, Key.Back);
        Assert.Equal("列表\r\n任务\r\n", editor.Text);
    }

    /// <summary>切换主题和字号后，验证实际模板使用新配色，且勾选状态不改变方框尺寸。</summary>
    private static void VerifyThemeAndFontSize(MarkdownEditorControl control)
    {
        var editor = control.TextEditor;
        editor.Text = "- [ ] 待办\n- [x] 完成";
        double previousWidth = GetCheckbox(editor, 1).Width;

        var theme = MarkdownTheme.Dark;
        theme.BaseFontSize = 24;
        control.Theme = theme;

        var uncheckedBox = GetCheckbox(editor, 1);
        var checkedBox = GetCheckbox(editor, 2);
        Assert.True(uncheckedBox.Width > previousWidth);
        Assert.Equal(uncheckedBox.Width, checkedBox.Width);
        Assert.Equal(uncheckedBox.Height, checkedBox.Height);

        uncheckedBox.ApplyTemplate();
        checkedBox.ApplyTemplate();
        var uncheckedBorder = Assert.IsType<Border>(uncheckedBox.Template.FindName("Box", uncheckedBox));
        var checkedBorder = Assert.IsType<Border>(checkedBox.Template.FindName("Box", checkedBox));
        Assert.Equal(theme.BulletColor, uncheckedBorder.BorderBrush);
        Assert.Equal(theme.LinkColor, checkedBorder.Background);
        var checkMark = Assert.IsAssignableFrom<UIElement>(checkedBox.Template.FindName("CheckMark", checkedBox));
        Assert.Equal(Visibility.Visible, checkMark.Visibility);
    }

    /// <summary>从 AvalonEdit 已排版的行中读取真正用于显示的复选框。</summary>
    private static CheckBox GetCheckbox(TextEditor editor, int lineNumber)
    {
        var line = editor.Document.GetLineByNumber(lineNumber);
        var visualLine = editor.TextArea.TextView.GetOrConstructVisualLine(line);
        return Assert.IsType<CheckBox>(Assert.Single(visualLine.Elements.OfType<InlineObjectElement>()).Element);
    }

    /// <summary>发送与编辑器键盘输入使用的同一个 WPF 路由事件。</summary>
    private static void PressKey(TextEditor editor, PresentationSource source, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        editor.TextArea.RaiseEvent(args);
        Assert.True(args.Handled);
    }
}

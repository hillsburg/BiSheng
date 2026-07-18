using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BiSheng.Latte.Controls.Navigation;
using BiSheng.Latte.ViewModels;

namespace BiSheng.Latte;

/// <summary>内联重命名：窗口级提交（导航交互已下沉至 UserControl）</summary>
public partial class MainWindow
{
    private void OnWindowPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (NavigationRenameHelper.IsClickOnInlineRenameTextBox(e.OriginalSource as DependencyObject))
            return;

        if (IsClickOnStandardInputControl(e.OriginalSource as DependencyObject))
            return;

        NavigationRenameHelper.CommitActiveRename(_vm);
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this), this);
    }

    private static bool IsClickOnStandardInputControl(DependencyObject? source)
    {
        for (var dep = source; dep != null; dep = System.Windows.Media.VisualTreeHelper.GetParent(dep))
        {
            if (dep is ComboBox or PasswordBox or RichTextBox)
                return true;
        }

        return false;
    }
}

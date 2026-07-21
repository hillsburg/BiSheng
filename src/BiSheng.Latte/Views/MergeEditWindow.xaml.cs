using System.Windows;

namespace BiSheng.Latte.Views;

/// <summary>
/// 手动合并：上下布局——只读本地/远端参考 + 可编辑合并结果；确认仅使用结果区
/// </summary>
public partial class MergeEditWindow : Window
{
    private readonly string _localContent;
    private readonly string _remoteContent;

    /// <summary>用户确认后的合并结果</summary>
    public string MergedContent { get; private set; } = string.Empty;

    public MergeEditWindow(string localContent, string remoteContent)
    {
        InitializeComponent();
        _localContent = localContent ?? string.Empty;
        _remoteContent = remoteContent ?? string.Empty;
        LocalRefBox.Text = _localContent;
        RemoteRefBox.Text = _remoteContent;
        // 默认以本地为起点，避免空白结果
        ResultBox.Text = _localContent;
        ResultBox.Focus();
        ResultBox.CaretIndex = ResultBox.Text.Length;
    }

    private void OnFillLocal(object sender, RoutedEventArgs e) => ResultBox.Text = _localContent;

    private void OnFillRemote(object sender, RoutedEventArgs e) => ResultBox.Text = _remoteContent;

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        MergedContent = ResultBox.Text;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

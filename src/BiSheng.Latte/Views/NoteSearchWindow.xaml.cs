using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using BiSheng.Latte.Services.Search;
using BiSheng.Latte.ViewModels;

namespace BiSheng.Latte.Views;

/// <summary>全文搜索三列弹窗</summary>
public partial class NoteSearchWindow : Window
{
    private readonly NoteSearchViewModel _viewModel;

    /// <summary>请求在主编辑器中打开笔记（由 MainViewModel 订阅）</summary>
    public event Action? RequestCloseAfterOpen;

    /// <summary>构造全文搜索窗口</summary>
    public NoteSearchWindow(INoteContentSearchService searchService, Action<BiSheng.Latte.Models.EditorNavigationIntent> openInEditor, string? initialQuery = null)
    {
        _viewModel = new NoteSearchViewModel(searchService, intent =>
        {
            openInEditor(intent);
            RequestCloseAfterOpen?.Invoke();
            DialogResult = true;
            Close();
        });

        if (!string.IsNullOrWhiteSpace(initialQuery))
        {
            _viewModel.SearchQuery = initialQuery;
        }

        DataContext = _viewModel;
        InitializeComponent();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NoteSearchViewModel.PreviewContent))
        {
            PreviewControl.SetContent(_viewModel.PreviewContent);
            ApplyPreviewHighlight();
            return;
        }

        if (e.PropertyName is nameof(NoteSearchViewModel.PreviewHighlightGeneration)
            or nameof(NoteSearchViewModel.SelectedHit))
        {
            ApplyPreviewHighlight();
            ScrollSelectedHitIntoView();
        }
    }

    private void ApplyPreviewHighlight()
    {
        if (_viewModel.SelectedHit == null || _viewModel.PreviewMarkdownSelectionLength <= 0)
        {
            return;
        }

        PreviewControl.HighlightAt(
            _viewModel.PreviewMarkdownCaret,
            _viewModel.PreviewMarkdownSelectionLength);
    }

    private void ScrollSelectedHitIntoView()
    {
        if (_viewModel.SelectedHit == null)
        {
            return;
        }

        HitList.UpdateLayout();
        if (HitList.ItemContainerGenerator.ContainerFromItem(_viewModel.SelectedHit) is ListBoxItem item)
        {
            item.BringIntoView();
        }
    }

    private void OnResultDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedResult != null && _viewModel.CanOpenInEditor)
        {
            _viewModel.OpenInEditorCommand.Execute(null);
        }
    }

    /// <summary>释放订阅</summary>
    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
    }
}

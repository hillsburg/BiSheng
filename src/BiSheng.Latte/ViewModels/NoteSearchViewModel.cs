using System.Collections.ObjectModel;
using BiSheng.Latte.Models;
using BiSheng.Latte.Services.Search;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BiSheng.Latte.ViewModels;

/// <summary>全文搜索弹窗视图模型</summary>
public partial class NoteSearchViewModel : ObservableObject
{
    private readonly INoteContentSearchService _searchService;
    private readonly Action<EditorNavigationIntent> _openInEditor;
    private CancellationTokenSource? _searchCts;

    /// <summary>构造全文搜索 VM</summary>
    public NoteSearchViewModel(
        INoteContentSearchService searchService,
        Action<EditorNavigationIntent> openInEditor)
    {
        _searchService = searchService;
        _openInEditor = openInEditor;
    }

    /// <summary>搜索关键词</summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>仅搜索标题</summary>
    [ObservableProperty]
    private bool _searchTitleOnly;

    /// <summary>是否正在搜索</summary>
    [ObservableProperty]
    private bool _isSearching;

    /// <summary>状态栏文案</summary>
    [ObservableProperty]
    private string _statusText = "输入关键词后按 Enter 或点击搜索";

    /// <summary>命中笔记列表是否有项</summary>
    [ObservableProperty]
    private bool _hasResults;

    /// <summary>当前笔记命中列表是否有项</summary>
    [ObservableProperty]
    private bool _hasHits;

    /// <summary>预览区是否有正文</summary>
    [ObservableProperty]
    private bool _hasPreview;

    /// <summary>笔记列表空状态文案</summary>
    [ObservableProperty]
    private string _resultsEmptyHint = "输入关键词后按 Enter 搜索";

    /// <summary>命中列表空状态文案</summary>
    [ObservableProperty]
    private string _hitsEmptyHint = "搜索后在此查看命中片段";

    /// <summary>命中笔记列表</summary>
    public ObservableCollection<NoteSearchResultItem> Results { get; } = new();

    /// <summary>当前笔记命中列表</summary>
    public ObservableCollection<NoteSearchHitItem> Hits { get; } = new();

    /// <summary>选中的笔记</summary>
    [ObservableProperty]
    private NoteSearchResultItem? _selectedResult;

    /// <summary>选中的命中</summary>
    [ObservableProperty]
    private NoteSearchHitItem? _selectedHit;

    /// <summary>预览 Markdown 正文</summary>
    [ObservableProperty]
    private string _previewContent = string.Empty;

    /// <summary>预览高亮请求（PlainText 坐标；标题命中时为 -1）</summary>
    [ObservableProperty]
    private int _previewHighlightOffset = -1;

    /// <summary>预览高亮长度</summary>
    [ObservableProperty]
    private int _previewHighlightLength;

    /// <summary>预览高亮序号（每次切换命中递增，驱动 UI 刷新）</summary>
    [ObservableProperty]
    private int _previewHighlightGeneration;

    /// <summary>预览 Markdown Caret</summary>
    [ObservableProperty]
    private int _previewMarkdownCaret;

    /// <summary>预览 Markdown 选区长度</summary>
    [ObservableProperty]
    private int _previewMarkdownSelectionLength;

    /// <summary>当前命中序号（1-based 展示）</summary>
    public int ActiveHitDisplayIndex => SelectedHit == null ? 0 : SelectedHit.Index + 1;

    /// <summary>当前笔记命中总数</summary>
    public int ActiveHitCount => Hits.Count;

    /// <summary>能否在编辑器中打开</summary>
    public bool CanOpenInEditor => SelectedResult != null && SelectedHit != null;

    /// <summary>执行搜索</summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = SearchQuery?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            StatusText = "请输入搜索关键词";
            ResultsEmptyHint = "请输入搜索关键词";
            NotifyListEmptyStates();
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        try
        {
            IsSearching = true;
            StatusText = "搜索中…";
            Results.Clear();
            Hits.Clear();
            SelectedResult = null;
            SelectedHit = null;
            PreviewContent = string.Empty;
            ClearPreviewHighlight();
            ResultsEmptyHint = "搜索中…";
            HitsEmptyHint = "搜索中…";
            NotifyListEmptyStates();

            var results = await _searchService.SearchAsync(query, SearchTitleOnly, token);
            foreach (var item in results)
            {
                Results.Add(item);
            }

            if (results.Count == 0)
            {
                StatusText = $"未找到包含「{query}」的笔记";
                ResultsEmptyHint = $"未找到包含「{query}」的笔记";
                HitsEmptyHint = "无匹配笔记";
                NotifyListEmptyStates();
                return;
            }

            StatusText = $"共 {results.Count} 篇笔记";
            ResultsEmptyHint = "输入关键词后按 Enter 搜索";
            SelectedResult = results[0];
            NotifyListEmptyStates();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusText = $"搜索失败：{ex.Message}";
            ResultsEmptyHint = "搜索失败，请稍后重试";
            NotifyListEmptyStates();
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>选中笔记后加载命中</summary>
    partial void OnSelectedResultChanged(NoteSearchResultItem? value)
    {
        _ = LoadHitsForResultAsync(value);
    }

    /// <summary>选中命中后更新预览高亮</summary>
    partial void OnSelectedHitChanged(NoteSearchHitItem? value)
    {
        ApplyPreviewHighlight(value);
        OnPropertyChanged(nameof(ActiveHitDisplayIndex));
        OnPropertyChanged(nameof(CanOpenInEditor));
        OpenInEditorCommand.NotifyCanExecuteChanged();
    }

    /// <summary>下一条命中</summary>
    [RelayCommand(CanExecute = nameof(HasMultipleHits))]
    private void NextHit()
    {
        if (SelectedHit == null || Hits.Count == 0)
        {
            return;
        }

        var current = Hits.IndexOf(SelectedHit);
        if (current < 0)
        {
            return;
        }

        SelectedHit = Hits[(current + 1) % Hits.Count];
    }

    /// <summary>上一条命中</summary>
    [RelayCommand(CanExecute = nameof(HasMultipleHits))]
    private void PreviousHit()
    {
        if (SelectedHit == null || Hits.Count == 0)
        {
            return;
        }

        var current = Hits.IndexOf(SelectedHit);
        if (current < 0)
        {
            return;
        }

        SelectedHit = Hits[(current - 1 + Hits.Count) % Hits.Count];
    }

    /// <summary>选中命中项</summary>
    [RelayCommand]
    private void SelectHit(NoteSearchHitItem? hit)
    {
        if (hit != null)
        {
            SelectedHit = hit;
        }
    }

    /// <summary>在编辑器中打开并关闭弹窗</summary>
    [RelayCommand(CanExecute = nameof(CanOpenInEditor))]
    private void OpenInEditor()
    {
        if (SelectedResult == null || SelectedHit == null)
        {
            return;
        }

        var query = SearchQuery?.Trim() ?? string.Empty;
        _openInEditor(new EditorNavigationIntent
        {
            NoteId = SelectedResult.NoteId,
            FolderId = SelectedResult.FolderId,
            PlainTextOffset = SelectedHit.PlainTextOffset,
            MatchLength = SelectedHit.Length,
            IsTitleHit = SelectedHit.IsTitleHit,
            MarkdownCaretOffset = SelectedHit.MarkdownCaretOffset,
            MarkdownSelectionLength = SelectedHit.MarkdownSelectionLength,
            Query = query
        });
    }

    private bool HasMultipleHits() => Hits.Count > 1;

    private async Task LoadHitsForResultAsync(NoteSearchResultItem? result)
    {
        Hits.Clear();
        SelectedHit = null;
        PreviewContent = string.Empty;
        ClearPreviewHighlight();
        HitsEmptyHint = result == null ? "搜索后在此查看命中片段" : "加载命中…";
        NotifyListEmptyStates();

        if (result == null)
        {
            return;
        }

        var query = SearchQuery?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            return;
        }

        try
        {
            var hits = await _searchService.GetHitsAsync(
                result.NoteId,
                query,
                SearchTitleOnly);

            foreach (var hit in hits)
            {
                Hits.Add(hit);
            }

            var content = await _searchService.GetNoteContentAsync(result.NoteId);
            PreviewContent = content ?? string.Empty;

            if (hits.Count > 0)
            {
                SelectedHit = hits[0];
                StatusText = $"「{result.Title}」共 {hits.Count} 处命中";
                HitsEmptyHint = "搜索后在此查看命中片段";
            }
            else
            {
                StatusText = $"「{result.Title}」无命中";
                HitsEmptyHint = "该笔记无匹配片段";
            }

            OnPropertyChanged(nameof(ActiveHitCount));
            NextHitCommand.NotifyCanExecuteChanged();
            PreviousHitCommand.NotifyCanExecuteChanged();
            OpenInEditorCommand.NotifyCanExecuteChanged();
            NotifyListEmptyStates();
        }
        catch (Exception ex)
        {
            StatusText = $"加载命中失败：{ex.Message}";
            HitsEmptyHint = "加载命中失败";
            NotifyListEmptyStates();
        }
    }

    private void ApplyPreviewHighlight(NoteSearchHitItem? hit)
    {
        if (hit == null)
        {
            ClearPreviewHighlight();
            NotifyListEmptyStates();
            return;
        }

        PreviewMarkdownCaret = hit.MarkdownCaretOffset;
        PreviewMarkdownSelectionLength = hit.MarkdownSelectionLength;
        PreviewHighlightOffset = hit.IsTitleHit ? -1 : hit.PlainTextOffset;
        PreviewHighlightLength = hit.IsTitleHit ? 0 : hit.Length;
        PreviewHighlightGeneration++;
        NotifyListEmptyStates();
    }

    private void ClearPreviewHighlight()
    {
        PreviewHighlightOffset = -1;
        PreviewHighlightLength = 0;
        PreviewMarkdownCaret = 0;
        PreviewMarkdownSelectionLength = 0;
        PreviewHighlightGeneration++;
    }

    /// <summary>刷新列表/预览空状态绑定</summary>
    private void NotifyListEmptyStates()
    {
        HasResults = Results.Count > 0;
        HasHits = Hits.Count > 0;
        HasPreview = !string.IsNullOrEmpty(PreviewContent);
    }
}

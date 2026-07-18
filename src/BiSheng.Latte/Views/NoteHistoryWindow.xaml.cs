using System.Windows;
using System.Windows.Controls;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services;
using BiSheng.Latte.ViewModels;
using BiSheng.Shared.Sync;

namespace BiSheng.Latte.Views;

/// <summary>
/// 笔记历史版本窗口：预览、恢复、手动保存当前版本、删除历史。
/// 在线时优先展示云端列表，否则展示本地 LocalNoteRevisions。
/// </summary>
public partial class NoteHistoryWindow : Window
{
    private readonly NoteRevisionService _revisions;
    private readonly EditorViewModel _editor;
    private readonly LocalNote _note;

    /// <summary>是否优先从服务端拉取历史（已连接且非离线模式）</summary>
    private readonly bool _useServer;

    private List<HistoryEntry> _entries = new();

    public NoteHistoryWindow(
        NoteRevisionService revisions,
        EditorViewModel editor,
        LocalNote note,
        bool useServer)
    {
        InitializeComponent();
        _revisions = revisions;
        _editor = editor;
        _note = note;
        _useServer = useServer;

        TitleText.Text = $"历史版本 · {note.Title}";
        Loaded += async (_, _) => await LoadAsync();
    }

    /// <summary>加载历史列表并选中最新一条</summary>
    private async Task LoadAsync()
    {
        try
        {
            _entries = await BuildEntryListAsync();
            RevisionList.ItemsSource = _entries;
            if (_entries.Count > 0)
            {
                EmptyStateText.Visibility = Visibility.Collapsed;
                PreviewEmptyText.Visibility = Visibility.Collapsed;
                RevisionList.SelectedIndex = 0;
            }
            else
            {
                EmptyStateText.Visibility = Visibility.Visible;
                PreviewEmptyText.Visibility = Visibility.Visible;
                PreviewBox.Text = string.Empty;
            }
        }
        catch (Exception ex)
        {
            PreviewBox.Text = $"加载失败: {ApiClientException.GetUserMessage(ex)}";
        }
    }

    /// <summary>在线优先云端，否则回退本地历史</summary>
    private async Task<List<HistoryEntry>> BuildEntryListAsync()
    {
        if (_useServer)
        {
            var server = await _revisions.FetchServerRevisionListAsync(_note.Id);
            if (server.Count > 0)
            {
                return server.Select(s => new HistoryEntry
                {
                    Id = s.Id,
                    RevisionNumber = s.RevisionNumber,
                    Title = s.Title,
                    ContentHash = s.ContentHash,
                    CreatedAt = s.CreatedAt,
                    Source = HistorySource.Server
                }).ToList();
            }
        }

        return _revisions.GetLocalRevisionList(_note.Id).Select(l => new HistoryEntry
        {
            Id = l.Id,
            RevisionNumber = l.RevisionNumber,
            Title = l.Title,
            ContentHash = l.ContentHash,
            CreatedAt = l.CreatedAt,
            Source = HistorySource.Local
        }).ToList();
    }

    /// <summary>用户手动保存当前编辑内容为本地历史版本</summary>
    private async void OnSaveCurrent(object sender, RoutedEventArgs e)
    {
        if (_editor.SaveManualRevision())
        {
            await LoadAsync();
            AppDialog.Info(this, "已保存为新的本地历史版本。", "历史版本");
        }
        else
        {
            AppDialog.Info(this, "内容与上一版相同，未产生新版本。", "历史版本");
        }
    }

    private async void OnRevisionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (RevisionList.SelectedItem is not HistoryEntry entry)
        {
            PreviewEmptyText.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            NoteRevisionDto? detail = entry.Source == HistorySource.Server
                ? await _revisions.FetchServerRevisionAsync(_note.Id, entry.Id)
                : _revisions.GetLocalRevision(_note.Id, entry.Id);

            PreviewBox.Text = detail?.Content ?? "(无法加载内容)";
            PreviewEmptyText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            PreviewBox.Text = $"加载失败: {ApiClientException.GetUserMessage(ex)}";
            PreviewEmptyText.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnRestore(object sender, RoutedEventArgs e)
    {
        if (RevisionList.SelectedItem is not HistoryEntry entry)
        {
            AppDialog.Info(this, "请先选择一个历史版本。", "提示");
            return;
        }

        if (!AppDialog.Confirm(this,
                $"确定将笔记恢复为第 {entry.RevisionNumber} 版？\n恢复后会写入当前笔记并产生新的历史版本。",
                "确认恢复"))
        {
            return;
        }

        try
        {
            RestoreButton.IsEnabled = false;

            if (entry.Source == HistorySource.Server)
            {
                var result = await _revisions.RestoreOnServerAsync(_note.Id, entry.Id);
                if (result == null)
                {
                    AppDialog.Error(this, "服务端恢复失败。", "错误");
                    return;
                }

                _editor.PersistRestore(result.Title, result.Content);
            }
            else
            {
                var detail = _revisions.GetLocalRevision(_note.Id, entry.Id);
                if (detail == null)
                {
                    AppDialog.Error(this, "无法读取本地历史版本。", "错误");
                    return;
                }

                _editor.PersistRestore(detail.Title, detail.Content);
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            AppDialog.Error(this, ex.Message, "恢复失败");
        }
        finally
        {
            RestoreButton.IsEnabled = true;
        }
    }

    private async void OnDeleteRevision(object sender, RoutedEventArgs e)
    {
        if (RevisionList.SelectedItem is not HistoryEntry entry)
        {
            AppDialog.Info(this, "请先选择一个历史版本。", "提示");
            return;
        }

        if (!AppDialog.ConfirmDanger(this, $"确定删除第 {entry.RevisionNumber} 版？", "确认删除"))
        {
            return;
        }

        try
        {
            if (entry.Source == HistorySource.Server)
            {
                await _revisions.DeleteServerRevisionAsync(_note.Id, entry.Id);
            }
            else
            {
                _revisions.DeleteLocalRevision(_note.Id, entry.Id);
            }

            await LoadAsync();
        }
        catch (Exception ex)
        {
            AppDialog.Error(this, ex.Message, "删除失败");
        }
    }

    private async void OnDeleteAll(object sender, RoutedEventArgs e)
    {
        if (!AppDialog.ConfirmDanger(this,
                "确定清空该笔记的全部历史版本？\n（不会删除笔记本身）",
                "确认清空"))
        {
            return;
        }

        try
        {
            if (_useServer)
            {
                await _revisions.DeleteAllServerRevisionsAsync(_note.Id);
            }

            _revisions.DeleteAllLocalRevisions(_note.Id);

            await LoadAsync();
        }
        catch (Exception ex)
        {
            AppDialog.Error(this, ex.Message, "清空失败");
        }
    }

    /// <summary>列表绑定项</summary>
    private sealed class HistoryEntry
    {
        public Guid Id { get; init; }
        public int RevisionNumber { get; init; }
        public string Title { get; init; } = string.Empty;
        public string ContentHash { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
        public HistorySource Source { get; init; }

        public string Label => $"第 {RevisionNumber} 版 · {CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}";

        public string SubLabel => Source == HistorySource.Server ? "云端" : "仅本地";
    }

    private enum HistorySource
    {
        /// <summary>仅存在于 local.db 的快照</summary>
        Local,

        /// <summary>已从服务端拉取的历史版本</summary>
        Server
    }
}

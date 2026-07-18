using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using BiSheng.Latte.Services;
using BiSheng.Latte.Services.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BiSheng.Latte.ViewModels;

/// <summary>
/// 笔记列表视图模型：管理笔记的增删查和重命名
/// 
/// 职责边界：
/// - 本地 DB 操作优先（创建、删除先写本地）
/// - 通过 LocalChangeTracker 记录变更，同步引擎异步处理
/// - 使用 NoteItemViewModel 包装 LocalNote，支持内联重命名
/// </summary>
public partial class NoteListViewModel : ObservableObject
{
    private readonly LocalChangeTracker _changeTracker;
    private readonly Func<LocalDbContext> _dbFactory;
    private readonly INavigationMutationPublisher _navigationPublisher;
    private readonly ExportService? _exportService;
    private NavigationViewModel? _navigation;
    private readonly INavigationFilterState _filterState;

    public ObservableCollection<NoteItemViewModel> Notes { get; } = new();

    [ObservableProperty]
    private NoteItemViewModel? _selectedNote;

    [ObservableProperty]
    private Guid? _currentFolderId;

    /// <summary>笔记选中事件：传递选中的笔记给编辑器；null 表示无选中项</summary>
    public event Action<LocalNote?>? OnNoteSelected;

    /// <summary>笔记即将删除（供编辑器在切换选中前落盘并清空）</summary>
    public event Action<Guid>? NoteDeleting;

    /// <summary>笔记创建事件：UI 层订阅后自动在编辑器中打开新笔记</summary>
    public event Action<LocalNote>? NoteCreated;

    /// <summary>笔记标题变更事件：编辑器需同步内存中的标题</summary>
    public event Action<Guid, string>? OnNoteTitleChanged;

    /// <summary>
    /// 初始化笔记列表视图模型。
    /// </summary>
    /// <param name="changeTracker">本地变更追踪器，用于记录增删改操作。</param>
    /// <param name="exportService">导出服务，为 null 时导出功能不可用。</param>
    public NoteListViewModel(
        LocalChangeTracker changeTracker,
        Func<LocalDbContext> dbFactory,
        INavigationMutationPublisher navigationPublisher,
        INavigationFilterState filterState,
        ExportService? exportService = null)
    {
        _changeTracker = changeTracker;
        _dbFactory = dbFactory;
        _navigationPublisher = navigationPublisher;
        _filterState = filterState;
        _exportService = exportService;
    }

    /// <summary>设置导航共享状态（搜索过滤）</summary>
    public void SetNavigation(NavigationViewModel navigation) => _navigation = navigation;

    private List<LocalNote> QueryNotesForFolder(Guid folderId)
    {
        using var db = _dbFactory();
        var notes = db.Notes.Where(n => n.FolderId == folderId && !n.IsDeleted).ToList();
        var search = _filterState.SearchText?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            notes = notes
                .Where(n => n.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase))
                .ToList();
        }

        return notes.OrderBy(n => n, Comparer<LocalNote>.Create(NavSortHelper.CompareNote)).ToList();
    }

    /// <summary>
    /// 加载指定文件夹下的所有笔记，按修改时间降序排列。
    /// </summary>
    /// <param name="folderId">目标文件夹 ID。</param>
    public void LoadNotes(Guid folderId)
    {
        CurrentFolderId = folderId;
        Notes.Clear();
        foreach (var n in QueryNotesForFolder(folderId))
            Notes.Add(new NoteItemViewModel(n));
    }

    /// <summary>
    /// 刷新当前文件夹的笔记列表。
    /// 保留选中项和重命名状态，解决同步刷新后高亮丢失的问题。
    /// </summary>
    public void Refresh()
    {
        if (!CurrentFolderId.HasValue) return;

        var selectedId = SelectedNote?.Id;
        var selectedIndex = selectedId.HasValue
            ? Notes.ToList().FindIndex(n => n.Id == selectedId.Value)
            : -1;
        var renamingId = Notes.FirstOrDefault(n => n.IsRenaming)?.Id;
        var renamingText = Notes.FirstOrDefault(n => n.IsRenaming)?.RenameText;

        var renamingItem = Notes.FirstOrDefault(n => n.IsRenaming);
        if (renamingItem != null) renamingItem.IsRenaming = false;

        using var db = _dbFactory();
        var notes = QueryNotesForFolder(CurrentFolderId.Value);
        Notes.Clear();
        foreach (var n in notes) Notes.Add(new NoteItemViewModel(n));

        if (selectedId.HasValue)
        {
            var match = Notes.FirstOrDefault(n => n.Id == selectedId.Value);
            if (match != null)
            {
                if (SelectedNote?.Id == match.Id)
                {
                    LocalNoteMerger.MergeFields(SelectedNote.Note, match.Note);
                }
                else
                {
                    SelectedNote = match;
                }

                if (renamingId.HasValue && match.Id == renamingId.Value)
                {
                    match.RenameText = renamingText ?? match.Title;
                    match.IsRenaming = true;
                }
            }
            else if (Notes.Count > 0)
            {
                var newIndex = selectedIndex >= 0
                    ? Math.Min(selectedIndex, Notes.Count - 1)
                    : 0;
                SelectedNote = Notes[newIndex];
            }
            else
            {
                SelectedNote = null;
            }
        }
    }

    /// <summary>
    /// 按 Id 选中笔记：始终从 Notes 集合解析项，避免树模式传入外部引用。
    /// </summary>
    public bool SelectNoteById(Guid noteId, Guid folderId)
    {
        if (CurrentFolderId != folderId)
        {
            LoadNotes(folderId);
        }

        var item = Notes.FirstOrDefault(n => n.Id == noteId);
        if (item == null)
        {
            Refresh();
            item = Notes.FirstOrDefault(n => n.Id == noteId);
        }

        if (item == null)
        {
            return false;
        }

        SelectedNote = item;
        return true;
    }

    /// <summary>
    /// 增量应用 Note 变更（并列模式、无搜索时）；失败返回 false
    /// </summary>
    public bool ApplyNavigationDelta(IReadOnlyList<NavigationChange> changes)
    {
        if (!CurrentFolderId.HasValue)
        {
            return true;
        }

        var folderId = CurrentFolderId.Value;
        var noteChanges = changes.Where(c => c.EntityType == EntityTypes.Note).ToList();
        if (noteChanges.Count == 0)
        {
            return true;
        }

        using var db = _dbFactory();
        foreach (var change in noteChanges)
        {
            var note = db.Notes.Find(change.EntityId);
            var existing = Notes.FirstOrDefault(n => n.Id == change.EntityId);

            if (change.Action == ChangeActions.Delete || note == null || note.IsDeleted)
            {
                if (existing != null)
                {
                    Notes.Remove(existing);
                }

                continue;
            }

            if (note.FolderId != folderId)
            {
                if (existing != null)
                {
                    Notes.Remove(existing);
                }

                continue;
            }

            if (existing == null)
            {
                InsertNoteSorted(new NoteItemViewModel(note));
            }
            else
            {
                LocalNoteMerger.MergeFields(existing.Note, note);
                existing.NotifyDisplayChanged();
                ReinsertNoteIfSortOrderChanged(existing);
            }
        }

        return true;
    }

    private void InsertNoteSorted(NoteItemViewModel item)
    {
        var comparer = Comparer<LocalNote>.Create(NavSortHelper.CompareNote);
        var index = 0;
        for (; index < Notes.Count; index++)
        {
            if (comparer.Compare(item.Note, Notes[index].Note) < 0)
            {
                break;
            }
        }

        Notes.Insert(index, item);
    }

    private void ReinsertNoteIfSortOrderChanged(NoteItemViewModel item)
    {
        var index = Notes.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        var comparer = Comparer<LocalNote>.Create(NavSortHelper.CompareNote);
        if (index > 0 && comparer.Compare(item.Note, Notes[index - 1].Note) < 0)
        {
            Notes.RemoveAt(index);
            InsertNoteSorted(item);
            return;
        }

        if (index < Notes.Count - 1 && comparer.Compare(item.Note, Notes[index + 1].Note) > 0)
        {
            Notes.RemoveAt(index);
            InsertNoteSorted(item);
        }
    }

    /// <summary>
    /// 选中笔记变化时，通知编辑器加载对应内容。
    /// </summary>
    partial void OnSelectedNoteChanged(NoteItemViewModel? value)
    {
        OnNoteSelected?.Invoke(value?.Note);
    }

    // ========================================================
    //  创建笔记命令
    // ========================================================

    /// <summary>
    /// 在指定文件夹中创建新笔记，使用默认名称并自动进入重命名模式。
    /// </summary>
    /// <param name="folderId">目标文件夹 ID。</param>
    /// <returns>创建的笔记实体；若未创建则返回 null。</returns>
    public LocalNote? CreateNoteInFolder(Guid folderId)
    {
        var defaultTitle = GenerateUniqueNoteName(folderId);

        var note = new LocalNote
        {
            Title = defaultTitle,
            FolderId = folderId,
            Content = $"# {defaultTitle}\n\n"
        };

        using var db = _dbFactory();
        db.Notes.Add(note);
        db.SaveChangesWithLock();

        _changeTracker.RecordChange(EntityTypes.Note, note.Id, ChangeActions.Create,
            SyncPayloadBuilder.Note(note.Title, note.Content, note.FolderId, note.IsFavorite, note.IsPinned));

        if (CurrentFolderId != folderId)
        {
            LoadNotes(folderId);
        }
        else
        {
            _navigationPublisher.NotifyNoteCreated(note.Id, folderId);
        }

        var item = Notes.FirstOrDefault(n => n.Id == note.Id);
        if (item != null)
        {
            SelectedNote = item;
            item.RenameText = item.Title;
            item.IsRenaming = true; // 创建后立即进入内联重命名
        }

        NoteCreated?.Invoke(note);
        return note;
    }

    /// <summary>
    /// 创建新笔记命令：在当前文件夹中使用默认名称创建并进入重命名模式。
    /// </summary>
    [RelayCommand]
    private void CreateNote()
    {
        if (!CurrentFolderId.HasValue) return;
        CreateNoteInFolder(CurrentFolderId.Value);
    }

    // ========================================================
    //  重命名笔记
    // ========================================================

    /// <summary>
    /// 开始重命名当前选中的笔记，将标题填入编辑框。
    /// </summary>
    public void StartRenaming()
    {
        if (SelectedNote == null) return;
        SelectedNote.RenameText = SelectedNote.Title;
        SelectedNote.IsRenaming = true;
    }

    /// <summary>
    /// 确认重命名：校验名称唯一性后将新标题持久化到数据库。
    /// </summary>
    /// <param name="item">正在重命名的笔记项。</param>
    public void CommitRename(NoteItemViewModel item)
    {
        // 防止失焦与窗口点击重复提交
        if (!item.IsRenaming) return;

        var newTitle = item.RenameText?.Trim();
        // 空名或未修改：保留默认/原标题并退出重命名
        if (string.IsNullOrEmpty(newTitle))
        {
            item.RenameText = item.Title;
            item.IsRenaming = false;
            return;
        }

        if (newTitle == item.Title) { item.IsRenaming = false; return; }

        if (IsNoteNameDuplicate(newTitle, item.FolderId, item.Id))
        {
            item.RenameText = item.Title;
            item.IsRenaming = false;
            return;
        }

        using var db = _dbFactory();
        var note = db.Notes.Find(item.Id);
        if (note != null)
        {
            note.Title = newTitle;
            note.UpdatedAt = DateTime.UtcNow;
            db.SaveChangesWithLock();
        }

        _changeTracker.RecordChange(EntityTypes.Note, item.Id, ChangeActions.Update,
            SyncPayloadBuilder.Note(newTitle, item.Note.Content, item.FolderId, note?.IsFavorite ?? false, note?.IsPinned ?? false));

        item.Note.Title = newTitle;
        OnNoteTitleChanged?.Invoke(item.Id, newTitle);

        item.IsRenaming = false;
        _navigationPublisher.NotifyNoteUpdated(item.Id, item.FolderId);
    }

    /// <summary>
    /// 取消重命名，恢复原始标题。
    /// </summary>
    /// <param name="item">正在重命名的笔记项。</param>
    public void CancelRename(NoteItemViewModel item)
    {
        item.RenameText = item.Title;
        item.IsRenaming = false;
    }

    /// <summary>切换笔记收藏状态</summary>
    public void ToggleFavorite(NoteItemViewModel item)
    {
        using var db = _dbFactory();
        var note = db.Notes.Find(item.Id);
        if (note == null) return;
        note.IsFavorite = !note.IsFavorite;
        note.UpdatedAt = DateTime.UtcNow;
        db.SaveChangesWithLock();
        item.Note.IsFavorite = note.IsFavorite;
        _changeTracker.RecordChange(EntityTypes.Note, note.Id, ChangeActions.Update,
            SyncPayloadBuilder.Note(note.Title, note.Content, note.FolderId, note.IsFavorite, note.IsPinned));
        _navigationPublisher.NotifyNoteUpdated(note.Id, note.FolderId, flagsChanged: true);
    }

    /// <summary>切换笔记置顶状态</summary>
    public void TogglePin(NoteItemViewModel item)
    {
        using var db = _dbFactory();
        var note = db.Notes.Find(item.Id);
        if (note == null) return;
        note.IsPinned = !note.IsPinned;
        note.UpdatedAt = DateTime.UtcNow;
        db.SaveChangesWithLock();
        item.Note.IsPinned = note.IsPinned;
        _changeTracker.RecordChange(EntityTypes.Note, note.Id, ChangeActions.Update,
            SyncPayloadBuilder.Note(note.Title, note.Content, note.FolderId, note.IsFavorite, note.IsPinned));
        _navigationPublisher.NotifyNoteUpdated(note.Id, note.FolderId, flagsChanged: true);
    }

    // ========================================================
    //  删除笔记命令
    // ========================================================

    /// <summary>
    /// 删除选中的笔记（软删除），并记录变更供同步引擎使用。
    /// </summary>
    [RelayCommand]
    private void DeleteSelectedNote()
    {
        if (SelectedNote == null) return;

        var deletedId = SelectedNote.Id;
        var folderId = SelectedNote.FolderId;
        NoteDeleting?.Invoke(deletedId);

        using var db = _dbFactory();
        var note = db.Notes.Find(SelectedNote.Id);
        if (note != null)
        {
            note.IsDeleted = true;
            note.DeletedAt = DateTime.UtcNow;
            note.UpdatedAt = DateTime.UtcNow;
            db.SaveChangesWithLock();
            _changeTracker.RecordChange(EntityTypes.Note, note.Id, ChangeActions.Delete);
        }

        _navigationPublisher.NotifyNoteDeleted(deletedId, folderId);
    }

    // ========================================================
    //  导出笔记命令
    // ========================================================

    /// <summary>
    /// 将选中笔记导出为 Markdown 文件，弹出目录选择对话框。
    /// </summary>
    [RelayCommand]
    private async Task ExportNoteAsMarkdownAsync()
    {
        if (SelectedNote == null || _exportService == null) return;
        var dialog = new OpenFolderDialog { Title = "选择 Markdown 导出目录" };
        if (dialog.ShowDialog() == true)
        {
            try { await _exportService.ExportNoteAsMarkdownAsync(SelectedNote.Note, dialog.FolderName); AppDialog.Success($"笔记已导出到:\n{dialog.FolderName}", "导出成功"); }
            catch (Exception ex) { AppDialog.Error($"导出失败: {ex.Message}", "导出失败"); }
        }
    }

    /// <summary>
    /// 将选中笔记导出为 Word 文档，弹出文件保存对话框。
    /// </summary>
    [RelayCommand]
    private async Task ExportNoteAsWordAsync()
    {
        if (SelectedNote == null || _exportService == null) return;
        var dialog = new SaveFileDialog { Title = "导出为 Word", Filter = "Word 文档 (*.docx)|*.docx|所有文件 (*.*)|*.*", DefaultExt = ".docx", FileName = ExportService.CreateTimestampedExportName(SelectedNote.Title) + ".docx" };
        if (dialog.ShowDialog() == true)
        {
            try { await _exportService.ExportNoteAsWordAsync(SelectedNote.Note, dialog.FileName); AppDialog.Success($"笔记已导出到:\n{dialog.FileName}", "导出成功"); }
            catch (Exception ex) { AppDialog.Error($"导出失败: {ex.Message}", "导出失败"); }
        }
    }

    /// <summary>
    /// 将选中笔记导出为 PDF 文件，弹出文件保存对话框。
    /// </summary>
    [RelayCommand]
    private async Task ExportNoteAsPdfAsync()
    {
        if (SelectedNote == null)
        {
            return;
        }

        if (_exportService == null)
        {
            AppDialog.Error("导出服务未初始化，无法导出 PDF。", "导出失败");
            return;
        }

        var dialog = new SaveFileDialog { Title = "导出为 PDF", Filter = "PDF 文件 (*.pdf)|*.pdf|所有文件 (*.*)|*.*", DefaultExt = ".pdf", FileName = ExportService.CreateTimestampedExportName(SelectedNote.Title) + ".pdf" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            await _exportService.ExportNoteAsPdfAsync(SelectedNote.Note, dialog.FileName);
            AppDialog.Success($"笔记已导出到:\n{dialog.FileName}", "导出成功");
        }
        catch (Exception ex)
        {
            AppDialog.Error($"导出失败: {ex.Message}", "导出失败");
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    // ========================================================
    //  笔记详情命令
    // ========================================================

    /// <summary>
    /// 显示笔记详情弹窗，包含标题、创建/修改时间、字符数、词数、行数等信息。
    /// </summary>
    [RelayCommand]
    private void ShowNoteDetails()
    {
        if (SelectedNote == null) return;
        var note = SelectedNote.Note;
        var charCount = note.Content?.Length ?? 0;
        var wordCount = string.IsNullOrWhiteSpace(note.Content) ? 0 : note.Content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        var lineCount = string.IsNullOrWhiteSpace(note.Content) ? 0 : note.Content.Split('\n').Length;
        AppDialog.Info($"标题：{note.Title}\n创建时间：{note.CreatedAt:yyyy-MM-dd HH:mm:ss}\n修改时间：{note.UpdatedAt:yyyy-MM-dd HH:mm:ss}\n字符数：{charCount}\n词数：{wordCount}\n行数：{lineCount}\n笔记 ID：{note.Id}", "笔记详情");
    }

    // ========================================================
    //  辅助方法
    // ========================================================

    /// <summary>
    /// 生成指定文件夹下的唯一笔记名称，同名时自动追加 _1、_2 等后缀。
    /// </summary>
    /// <param name="folderId">目标文件夹 ID。</param>
    /// <param name="baseName">基础名称，默认"新建笔记"。</param>
    /// <returns>唯一可用的笔记名称。</returns>
    public string GenerateUniqueNoteName(Guid folderId, string baseName = "新建笔记")
    {
        using var db = _dbFactory();
        var existingNames = db.Notes.Where(n => n.FolderId == folderId && !n.IsDeleted).Select(n => n.Title).ToHashSet();
        if (!existingNames.Contains(baseName)) return baseName;
        for (int i = 1; ; i++) { var candidate = $"{baseName}_{i}"; if (!existingNames.Contains(candidate)) return candidate; }
    }

    /// <summary>
    /// 检查同文件夹下是否存在同名笔记（排除指定 ID）。
    /// </summary>
    /// <param name="name">待检查的标题。</param>
    /// <param name="folderId">所属文件夹 ID。</param>
    /// <param name="excludeId">需排除的笔记 ID（自身）。</param>
    /// <returns>存在同名返回 true，否则 false。</returns>
    private bool IsNoteNameDuplicate(string name, Guid folderId, Guid excludeId)
    {
        using var db = _dbFactory();
        return db.Notes.Any(n => n.FolderId == folderId && n.Title == name && n.Id != excludeId && !n.IsDeleted);
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Models;
using BiSheng.Latte.Services;
using BiSheng.Latte.Services.Navigation;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiSheng.Latte.ViewModels;

/// <summary>
/// 编辑器视图模型：管理当前编辑的笔记状态
///
/// 职责边界：
/// - 仅依赖 LocalChangeTracker 记录变更（纯本地操作）
/// - 不依赖 SyncService（同步是独立的后台引擎）
/// - 本地保存是首要职责，同步队列是附带记录
///
/// 性能：
/// - 800ms 自动保存在后台线程落盘，不阻塞 UI 输入
/// - 切换笔记 / 失焦 / 退出仍同步落盘，保证数据安全
/// - 笔记 + PendingChanges 单次事务写入
/// </summary>
public partial class EditorViewModel : ObservableObject
{
    private readonly LocalChangeTracker _changeTracker;
    private readonly NoteRevisionService _noteRevisions;
    private readonly Func<LocalDbContext> _dbFactory;

    /// <summary>距上次按键不足此时间视为「正在编辑」，同步完成时可延迟刷新导航</summary>
    public static readonly TimeSpan RecentEditWindow = TimeSpan.FromSeconds(4);

    [ObservableProperty]
    private string _editorContent = string.Empty;

    [ObservableProperty]
    private string _noteTitle = string.Empty;

    [ObservableProperty]
    private LocalNote? _currentNote;

    /// <summary>自动保存防抖定时器：编辑后 800ms 触发本地保存</summary>
    private System.Timers.Timer? _autoSaveTimer;

    /// <summary>停笔空闲后尝试记一条本地历史（与自动保存解耦）</summary>
    private System.Timers.Timer? _revisionIdleTimer;

    /// <summary>当前笔记上次成功保存的内容哈希</summary>
    private string? _lastSavedContentHash;

    /// <summary>最后一次内容变更时间（UTC）</summary>
    private DateTime _lastEditUtc = DateTime.MinValue;

    /// <summary>Markdown 编辑器是否持有键盘焦点</summary>
    private bool _editorHasFocus;

    /// <summary>近期是否有编辑活动（供 MainViewModel 决定是否延迟导航刷新）</summary>
    public bool IsRecentlyEditing =>
        CurrentNote != null && DateTime.UtcNow - _lastEditUtc < RecentEditWindow;

    /// <summary>编辑器内容与上次落盘不一致（含仅改 UI 未触发自动保存的情况）</summary>
    public bool IsDirty =>
        CurrentNote != null
        && ComputeContentHash(EditorContent) != (_lastSavedContentHash ?? string.Empty);

    /// <summary>
    /// 用户是否处于编辑会话中（焦点在编辑器 / 近期有按键 / 有未落盘修改）。
    /// 同步完成刷新导航时必须避开，否则会重写 NoteEditor.Text 导致光标跳动
    /// </summary>
    public bool IsEditingSessionActive => _editorHasFocus || IsRecentlyEditing || IsDirty;

    /// <summary>由 MainWindow 在编辑器 GotFocus / LostFocus 时更新</summary>
    public void SetEditorFocus(bool focused)
    {
        _editorHasFocus = focused;
    }

    /// <summary>进行中的后台自动保存代数，用于丢弃过期任务</summary>
    private int _autoSaveGeneration;

    /// <summary>串行化 Notes/Pending 落盘，避免自动保存与 ForceSave 交错写库</summary>
    private readonly object _persistLock = new();

    /// <summary>每篇笔记的编辑器状态（滚动偏移 + 光标偏移），切换时保存/恢复</summary>
    private readonly Dictionary<Guid, (double ScrollOffset, int CaretOffset)> _savedPositions = new();

    public EditorViewModel(
        LocalChangeTracker changeTracker,
        NoteRevisionService noteRevisions,
        Func<LocalDbContext> dbFactory)
    {
        _changeTracker = changeTracker;
        _noteRevisions = noteRevisions;
        _dbFactory = dbFactory;
    }

    public void SavePosition(Guid noteId, double scrollOffset, int caretOffset)
    {
        _savedPositions[noteId] = (scrollOffset, caretOffset);
    }

    public (double ScrollOffset, int CaretOffset)? GetSavedPosition(Guid noteId)
    {
        return _savedPositions.TryGetValue(noteId, out var pos) ? pos : null;
    }

    private EditorNavigationIntent? _pendingNavigation;

    /// <summary>全文搜索跳转前设置（须在 SelectNoteById 之前）</summary>
    public void SetPendingNavigation(EditorNavigationIntent intent)
    {
        _pendingNavigation = intent;
    }

    /// <summary>主窗口加载笔记后消费一次性定位意图</summary>
    public EditorNavigationIntent? ConsumePendingNavigation(Guid noteId)
    {
        if (_pendingNavigation?.NoteId != noteId)
        {
            return null;
        }

        var intent = _pendingNavigation;
        _pendingNavigation = null;
        return intent;
    }

    /// <summary>清除未消费的跳转意图</summary>
    public void ClearPendingNavigation()
    {
        _pendingNavigation = null;
    }

    public void LoadNote(LocalNote note, string? editorText = null)
    {
        LogHelper.Debug("加载笔记: {0} (ID: {1})", note.Title, note.Id);

        _autoSaveTimer?.Stop();
        _revisionIdleTimer?.Stop();
        Interlocked.Increment(ref _autoSaveGeneration);

        if (CurrentNote != null && CurrentNote.Id != note.Id)
        {
            var leaving = CurrentNote;
            var leavingContent = editorText ?? EditorContent;
            SaveIfDirtySync(editorText, notifySync: true);
            TryRecordRevisionCheckpoint(leaving.Id, leaving.Title, leavingContent, LocalRevisionTrigger.NoteSwitch);
        }
        else
        {
            SaveIfDirtySync(editorText, notifySync: true);
        }

        using (var db = _dbFactory())
        {
            var fromDb = db.Notes.Find(note.Id);
            if (fromDb == null)
            {
                return;
            }

            // 防止同步回声/竞态用空 DB 内容覆盖入参 note（与 TryReloadCurrentNoteFromDb 一致）：
            // 场景——NoteList 缓存的 note.Content 非空，但同步已把 DB 改空（另一台设备清空内容、
            // 且本机无 pending 触发不了 ApplyRemoteNotePayload 的空内容保护）。
            // 此时信任入参快照，避免编辑器被清空
            if (string.IsNullOrEmpty(fromDb.Content) && !string.IsNullOrEmpty(note.Content))
            {
                fromDb.Content = note.Content;
            }

            LocalNoteMerger.MergeFields(note, fromDb);
        }

        CurrentNote = note;
        NoteTitle = note.Title;
        EditorContent = note.Content ?? string.Empty;
        _lastSavedContentHash = ComputeContentHash(EditorContent);
        _lastEditUtc = DateTime.MinValue;

        _autoSaveTimer?.Dispose();
        _autoSaveTimer = new System.Timers.Timer(800);
        _autoSaveTimer.AutoReset = false;
        _autoSaveTimer.Elapsed += (_, _) =>
        {
            Application.Current?.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                () => _ = FlushAutoSaveAsync());
        };

        ResetRevisionIdleTimer();
    }

    /// <summary>当前笔记被删除或列表无选中项时清空编辑器状态</summary>
    public void ClearNote()
    {
        _autoSaveTimer?.Stop();
        _revisionIdleTimer?.Stop();
        Interlocked.Increment(ref _autoSaveGeneration);
        CurrentNote = null;
        NoteTitle = string.Empty;
        EditorContent = string.Empty;
        _lastSavedContentHash = null;
        _lastEditUtc = DateTime.MinValue;
    }

    /// <summary>远端同步后刷新当前笔记（用户未在编辑且内容与 DB 一致时才覆盖 UI）</summary>
    public bool TryReloadCurrentNoteFromDb()
    {
        if (CurrentNote == null || IsEditingSessionActive)
        {
            return false;
        }

        using var db = _dbFactory();
        var fromDb = db.Notes.Find(CurrentNote.Id);
        if (fromDb == null || fromDb.IsDeleted)
        {
            ClearNote();
            ContentRestored?.Invoke();
            return true;
        }

        // 防止同步回声/竞态用空内容覆盖编辑器
        if (string.IsNullOrEmpty(fromDb.Content) && !string.IsNullOrEmpty(EditorContent))
        {
            return false;
        }

        var dbContent = fromDb.Content ?? string.Empty;

        // 编辑器当前正文与 DB 一致：只同步元数据，不触发 ContentRestored（重写 Text 会重置光标）
        if (string.Equals(dbContent, EditorContent, StringComparison.Ordinal))
        {
            if (fromDb.Title != CurrentNote.Title)
            {
                NoteTitle = fromDb.Title;
            }

            LocalNoteMerger.MergeFields(CurrentNote, fromDb);
            _lastSavedContentHash = ComputeContentHash(EditorContent);
            return false;
        }

        LocalNoteMerger.MergeFields(CurrentNote, fromDb);
        NoteTitle = CurrentNote.Title;
        EditorContent = dbContent;
        _lastSavedContentHash = ComputeContentHash(EditorContent);
        ContentRestored?.Invoke();
        return true;
    }

    /// <summary>数据安全档位变更后重建空闲快照定时器</summary>
    public void RefreshRevisionIdleTimer() => ResetRevisionIdleTimer();

    private void ResetRevisionIdleTimer()
    {
        _revisionIdleTimer?.Stop();
        _revisionIdleTimer?.Dispose();
        _revisionIdleTimer = new System.Timers.Timer(
            TimeSpan.FromMinutes(
                EffectiveRevisionPolicy.IdleSnapshotMinutes(DataSafetySettings.Load())).TotalMilliseconds);
        _revisionIdleTimer.AutoReset = false;
        _revisionIdleTimer.Elapsed += (_, _) =>
        {
            Application.Current?.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                OnRevisionIdleElapsed);
        };
    }

    private void OnRevisionIdleElapsed()
    {
        if (CurrentNote == null)
        {
            return;
        }

        TryRecordRevisionCheckpoint(
            CurrentNote.Id,
            CurrentNote.Title,
            EditorContent,
            LocalRevisionTrigger.Idle);
    }

    public void OnContentChanged(string newContent)
    {
        EditorContent = newContent;
        _lastEditUtc = DateTime.UtcNow;
        _autoSaveTimer?.Stop();
        _autoSaveTimer?.Start();
        _revisionIdleTimer?.Stop();
        _revisionIdleTimer?.Start();
    }

    public void ForceSave(string? editorText = null, bool checkpointRevision = false)
    {
        _autoSaveTimer?.Stop();
        _revisionIdleTimer?.Stop();
        Interlocked.Increment(ref _autoSaveGeneration);
        SaveIfDirtySync(editorText, notifySync: true);

        if (checkpointRevision && CurrentNote != null)
        {
            TryRecordRevisionCheckpoint(
                CurrentNote.Id,
                CurrentNote.Title,
                editorText ?? EditorContent,
                LocalRevisionTrigger.AppExit);
        }
    }

    public bool SaveManualRevision(string? editorText = null)
    {
        ForceSave(editorText);
        if (CurrentNote == null)
        {
            return false;
        }

        return _noteRevisions.RecordManualLocalRevision(
            CurrentNote.Id,
            CurrentNote.Title,
            editorText ?? EditorContent);
    }

    public void ApplyRenamedTitle(Guid noteId, string newTitle)
    {
        if (CurrentNote?.Id != noteId)
        {
            return;
        }

        CurrentNote.Title = newTitle;
        NoteTitle = newTitle;
    }

    /// <summary>后台自动保存：快照在 UI 线程采集，DB 写入在线程池</summary>
    private async Task FlushAutoSaveAsync()
    {
        var snapshot = TryCaptureSaveSnapshot(null);
        if (snapshot == null)
        {
            return;
        }

        var generation = Interlocked.Increment(ref _autoSaveGeneration);
        await Task.Run(() => PersistSnapshot(snapshot, generation, notifySync: true)).ConfigureAwait(false);
    }

    /// <summary>同步落盘（切换笔记、失焦、退出等必须完成的场景）</summary>
    private void SaveIfDirtySync(string? editorText, bool notifySync)
    {
        var snapshot = TryCaptureSaveSnapshot(editorText);
        if (snapshot == null)
        {
            return;
        }

        PersistSnapshot(snapshot, generation: int.MaxValue, notifySync);
    }

    private NoteSaveSnapshot? TryCaptureSaveSnapshot(string? editorText)
    {
        if (CurrentNote == null)
        {
            return null;
        }

        var content = editorText ?? EditorContent;
        var hash = ComputeContentHash(content);
        if (hash == _lastSavedContentHash)
        {
            return null;
        }

        return new NoteSaveSnapshot(
            CurrentNote.Id,
            CurrentNote.Title,
            content,
            hash,
            CurrentNote.FolderId,
            CurrentNote.IsFavorite,
            CurrentNote.IsPinned);
    }

    /// <summary>单次事务：更新 Notes + 合并 PendingChanges。串行落盘；过时的自动保存快照直接丢弃。</summary>
    private void PersistSnapshot(NoteSaveSnapshot snapshot, int generation, bool notifySync)
    {
        lock (_persistLock)
        {
            // ForceSave / ClearNote / 更新一轮自动保存会抬高 generation，丢弃过时快照，避免覆盖新内容
            if (generation != int.MaxValue
                && generation != Volatile.Read(ref _autoSaveGeneration))
            {
                LogHelper.Trace("跳过过时自动保存快照: {0}", snapshot.Title);
                return;
            }

            using var db = _dbFactory();
            var note = db.Notes.Find(snapshot.NoteId);
            if (note == null)
            {
                return;
            }

            var updatedAt = DateTime.UtcNow;
            note.Content = snapshot.Content;
            note.Title = snapshot.Title;
            note.UpdatedAt = updatedAt;

            _changeTracker.ApplyPendingChange(
                db,
                EntityTypes.Note,
                snapshot.NoteId,
                ChangeActions.Update,
                SyncPayloadBuilder.Note(
                    snapshot.Title,
                    snapshot.Content,
                    snapshot.FolderId,
                    snapshot.IsFavorite,
                    snapshot.IsPinned),
                journalTitle: snapshot.Title,
                journalContent: snapshot.Content);

            db.SaveChangesWithLock();

            if (notifySync)
            {
                _changeTracker.NotifyChangeRecorded();
            }

            LogHelper.Trace("笔记已保存: {0}", snapshot.Title);

            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (generation != int.MaxValue && generation != Volatile.Read(ref _autoSaveGeneration))
                {
                    return;
                }

                if (CurrentNote?.Id != snapshot.NoteId)
                {
                    return;
                }

                var liveHash = ComputeContentHash(EditorContent);
                if (liveHash != snapshot.ContentHash)
                {
                    // 落盘期间用户继续输入：保留 dirty 状态，等待下一次自动保存
                    CurrentNote.UpdatedAt = updatedAt;
                    return;
                }

                CurrentNote.Content = snapshot.Content;
                CurrentNote.UpdatedAt = updatedAt;
                _lastSavedContentHash = snapshot.ContentHash;
            });
        }
    }

    public void ApplyRestoredContent(string title, string content)
    {
        if (CurrentNote == null)
        {
            return;
        }

        CurrentNote.Title = title;
        CurrentNote.Content = content;
        NoteTitle = title;
        EditorContent = content ?? string.Empty;
        _lastSavedContentHash = ComputeContentHash(EditorContent);
        _lastEditUtc = DateTime.UtcNow;
    }

    public void PersistRestore(string title, string content)
    {
        if (CurrentNote == null)
        {
            return;
        }

        var hash = ComputeContentHash(content);
        var snapshot = new NoteSaveSnapshot(
            CurrentNote.Id,
            title,
            content,
            hash,
            CurrentNote.FolderId,
            CurrentNote.IsFavorite,
            CurrentNote.IsPinned);

        PersistSnapshot(snapshot, generation: int.MaxValue, notifySync: true);
        ApplyRestoredContent(title, content);
        _noteRevisions.TryRecordLocalRevision(CurrentNote.Id, title, content, LocalRevisionTrigger.Restore);
        ContentRestored?.Invoke();
    }

    private void TryRecordRevisionCheckpoint(
        Guid noteId,
        string title,
        string content,
        LocalRevisionTrigger trigger)
    {
        _noteRevisions.TryRecordLocalRevision(noteId, title, content, trigger);
    }

    public event Action? ContentRestored;

    private static string ComputeContentHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty));
        return Convert.ToHexString(bytes);
    }

    private sealed record NoteSaveSnapshot(
        Guid NoteId,
        string Title,
        string Content,
        string ContentHash,
        Guid FolderId,
        bool IsFavorite,
        bool IsPinned);
}

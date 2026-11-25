using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BiSheng.Latte.Models;
using BiSheng.Latte.Services;
using BiSheng.Latte.ViewModels;

namespace BiSheng.Latte;

/// <summary>编辑器宿主：Markdown 控件与 ViewModel / 图片 / 大纲的桥接</summary>
public partial class MainWindow
{
    private LatteImageResolver? _imageResolver;
    private bool _isLoadingNote;
    private DispatcherTimer? _outlineTimer;
    private int _outlineRefreshVersion;

    /// <summary>初始化编辑器事件、笔记切换与图片解析</summary>
    private void InitializeEditorHost()
    {
        // 按键只标记过期，正文在自动保存等真正需要时才取（长笔记避免每键复制全文）
        _vm.Editor.AttachLiveTextProvider(() => NoteEditor.Text);
        NoteEditor.TextEditor.TextChanged += (_, _) =>
        {
            // 等待去抖期间就淘汰旧快照，避免旧后台任务把大纲回退到编辑前。
            Interlocked.Increment(ref _outlineRefreshVersion);
            if (!_isLoadingNote)
            {
                _vm.Editor.MarkContentChanged();
                ScheduleOutlineRefresh();
            }
        };

        NoteEditor.TextEditor.GotFocus += (_, _) => _vm.Editor.SetEditorFocus(true);
        NoteEditor.TextEditor.LostFocus += (_, _) =>
        {
            _vm.Editor.SetEditorFocus(false);
            _vm.Editor.ForceSave(NoteEditor.Text);
        };

        _vm.Editor.ContentRestored += () => Dispatcher.Invoke(() =>
        {
            var newText = _vm.Editor.EditorContent;
            if (NoteEditor.Text == newText)
            {
                return;
            }

            var caret = NoteEditor.TextEditor.CaretOffset;
            var scroll = NoteEditor.TextEditor.TextArea.TextView.VerticalOffset;

            _isLoadingNote = true;
            NoteEditor.Text = newText;
            _isLoadingNote = false;

            NoteEditor.TextEditor.CaretOffset = Math.Min(caret, NoteEditor.TextEditor.Document.TextLength);
            Dispatcher.BeginInvoke(() =>
            {
                NoteEditor.TextEditor.ScrollToVerticalOffset(scroll);
            }, DispatcherPriority.Background);

            RequestOutlineRefresh();
        });

        _vm.NoteSwitching += note =>
        {
            if (_vm.Editor.CurrentNote != null)
            {
                _vm.Editor.SavePosition(
                    _vm.Editor.CurrentNote.Id,
                    NoteEditor.TextEditor.TextArea.TextView.VerticalOffset,
                    NoteEditor.TextEditor.CaretOffset);
            }

            _isLoadingNote = true;
            ResetOutline();

            _vm.Editor.LoadNote(note, NoteEditor.Text);
            NoteEditor.Text = _vm.Editor.EditorContent;

            var searchNav = _vm.Editor.ConsumePendingNavigation(note.Id);
            if (searchNav != null)
            {
                var pos = searchNav.MarkdownSelectionLength > 0
                    ? new BiSheng.Latte.Services.Search.NoteSearchNavigation.CaretPosition(
                        searchNav.MarkdownCaretOffset,
                        searchNav.MarkdownSelectionLength)
                    : BiSheng.Latte.Services.Search.NoteSearchNavigation.MapToCaretOffset(
                        note.Content ?? string.Empty,
                        searchNav.PlainTextOffset,
                        searchNav.MatchLength,
                        searchNav.Query,
                        searchNav.IsTitleHit);

                NoteEditor.TextEditor.CaretOffset = Math.Min(
                    pos.CaretOffset,
                    NoteEditor.TextEditor.Document.TextLength);
                NoteEditor.TextEditor.SelectionLength = Math.Min(
                    pos.SelectionLength,
                    NoteEditor.TextEditor.Document.TextLength - NoteEditor.TextEditor.CaretOffset);

                var location = NoteEditor.TextEditor.Document.GetLocation(NoteEditor.TextEditor.CaretOffset);
                Dispatcher.BeginInvoke(() =>
                {
                    NoteEditor.TextEditor.ScrollTo(location.Line, location.Column);
                    NoteEditor.TextEditor.TextArea.Caret.BringCaretToView();
                    NoteEditor.TextEditor.Focus();
                }, DispatcherPriority.Loaded);
            }
            else
            {
                var savedPos = _vm.Editor.GetSavedPosition(note.Id);
                if (savedPos != null)
                {
                    NoteEditor.TextEditor.CaretOffset = Math.Min(
                        savedPos.Value.CaretOffset,
                        NoteEditor.TextEditor.Document.TextLength);
                    Dispatcher.BeginInvoke(() =>
                    {
                        NoteEditor.TextEditor.ScrollToVerticalOffset(savedPos.Value.ScrollOffset);
                    }, DispatcherPriority.Background);
                }
            }

            _isLoadingNote = false;
            RequestOutlineRefresh();
        };

        _vm.NoteClosed += () => Dispatcher.Invoke(() =>
        {
            _isLoadingNote = true;
            ResetOutline();
            NoteEditor.Text = string.Empty;
            _isLoadingNote = false;
        });

        _vm.NoteList.NoteCreated += note =>
        {
            Dispatcher.Invoke(() =>
            {
                _isLoadingNote = true;
                ResetOutline();
                _vm.Editor.LoadNote(note, NoteEditor.Text);
                NoteEditor.Text = _vm.Editor.EditorContent;
                _isLoadingNote = false;
                RequestOutlineRefresh();
            });
        };

        _imageResolver = new LatteImageResolver(_vm.ImageSync);
        NoteEditor.ImageResolver = _imageResolver;

        NoteEditor.ImagePasted += (imageId, filePath) =>
        {
            var noteId = _vm.Editor.CurrentNote?.Id;
            if (noteId != null)
            {
                _vm.ImageStorage.RecordImage(imageId, noteId.Value, filePath);
            }
        };

        _imageResolver.OnImageResolved += _ =>
        {
            Dispatcher.Invoke(() =>
            {
                NoteEditor.TextEditor.TextArea.TextView.Redraw();
            });
        };
    }

    /// <summary>从磁盘或传入快照加载外观设置并应用到全局与编辑器</summary>
    /// <param name="settings">若提供则使用内存中的设置（实时预览）；否则从磁盘加载</param>
    internal void ApplyAppearanceSettings(AppearanceSettings? settings = null)
    {
        settings ??= AppearanceSettings.Load();
        var theme = ThemeManager.Resolve(settings);

        theme.ApplyToResources(Application.Current.Resources);

        var fontStack = FontCatalog.ResolveEffectiveStack(theme, settings);
        var fontFamily = FontCatalog.CreateFontFamily(fontStack);
        FontCatalog.ApplyContentFontToResources(Application.Current.Resources, fontFamily);

        // 先设 Theme（含 BaseFontFamily），再设 EditorFontFamily，确保 AvalonEdit 与渲染层一致
        var mdTheme = theme.ToMarkdownTheme();
        mdTheme.Heading1FontSize = settings.H1Size;
        mdTheme.Heading2FontSize = settings.H2Size;
        mdTheme.Heading3FontSize = settings.H3Size;
        mdTheme.Heading4FontSize = settings.H4Size;
        mdTheme.Heading5FontSize = settings.H5Size;
        mdTheme.Heading6FontSize = settings.H6Size;
        mdTheme.BaseFontSize = settings.BodySize;
        mdTheme.BaseFontFamily = fontFamily;
        NoteEditor.Theme = mdTheme;

        NoteEditor.EditorFontFamily = fontFamily;
        NoteEditor.LineSpacing = settings.LineSpacing;
        NoteEditor.EditorFontSize = settings.BodySize;

        // 应用导航布局模式
        _vm.ApplyLayoutMode(settings.LayoutMode);
        _vm.ApplyToolbarPlacement(settings.ToolbarPlacement);
        ApplyToolbarVisibilityBehavior(settings.ToolbarVisibilityMode);
        ApplyStatusBarVisibilityBehavior(settings.StatusBarVisibilityMode);
        _vm.UpdateCloseWindowTooltip(settings.CloseToTray);

        LogHelper.Debug("外观设置已应用: 主题={0}, 字体={1}, 行高={2}, 正文={3}pt",
            settings.ActiveTheme,
            fontStack,
            settings.LineSpacing,
            settings.BodySize);
    }

    /// <summary>切换或关闭笔记时清空大纲，并阻止旧笔记的异步结果回写。</summary>
    private void ResetOutline()
    {
        _outlineTimer?.Stop();
        Interlocked.Increment(ref _outlineRefreshVersion);
        _vm.Outline.Reset();
    }

    /// <summary>延迟刷新大纲（去抖；长文延长间隔）</summary>
    private void ScheduleOutlineRefresh()
    {
        if (!_vm.IsOutlinePanelVisible || _isLoadingNote)
        {
            return;
        }

        _outlineTimer ??= new DispatcherTimer();
        _outlineTimer.Interval = TimeSpan.FromMilliseconds(GetOutlineDebounceMs());
        _outlineTimer.Stop();
        _outlineTimer.Tick -= OnOutlineTimerTick;
        _outlineTimer.Tick += OnOutlineTimerTick;
        _outlineTimer.Start();
    }

    private int GetOutlineDebounceMs()
    {
        var lineCount = NoteEditor.TextEditor.Document.LineCount;
        return lineCount switch
        {
            > 5000 => 2000,
            > 2000 => 1500,
            > 800 => 1200,
            _ => 800
        };
    }

    private void OnOutlineTimerTick(object? sender, EventArgs e)
    {
        _outlineTimer?.Stop();
        RefreshOutline();
    }

    /// <summary>笔记加载后在后台优先级请求刷新，让编辑器先完成布局。</summary>
    private void RequestOutlineRefresh()
    {
        if (!_vm.IsOutlinePanelVisible)
        {
            return;
        }

        Dispatcher.BeginInvoke(RefreshOutline, DispatcherPriority.Background);
    }

    /// <summary>从编辑器 Markdown 异步刷新大纲 ViewModel</summary>
    private void RefreshOutline()
    {
        if (!_vm.IsOutlinePanelVisible)
        {
            return;
        }

        _ = RefreshOutlineAsync();
    }

    private async Task RefreshOutlineAsync()
    {
        var version = Interlocked.Increment(ref _outlineRefreshVersion);

        // 后台只读取不可变文本；锚点和绑定集合留在 UI 线程上更新。
        var document = NoteEditor.TextEditor.Document;
        var text = document.Text;
        var flatItems = await Task.Run(() => OutlineViewModel.ParseFlatHeadings(text))
            .ConfigureAwait(true);

        if (version != Volatile.Read(ref _outlineRefreshVersion)
            || _isLoadingNote || !_vm.IsOutlinePanelVisible
            || !ReferenceEquals(document, NoteEditor.TextEditor.Document))
        {
            return;
        }

        _vm.Outline.ApplyHeadings(document, flatItems);
    }

    /// <summary>双击大纲条目跳转到编辑器对应行</summary>
    private void OnOutlineTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (OutlineTree.SelectedItem is not OutlineItem item)
        {
            return;
        }

        var editor = NoteEditor.TextEditor;
        var line = _vm.Outline.FindHeadingLine(editor.Document, item);
        if (line == null)
        {
            return;
        }

        editor.ScrollToLine(line.LineNumber);
        editor.CaretOffset = line.Offset;
        editor.TextArea.Caret.BringCaretToView();
    }
}

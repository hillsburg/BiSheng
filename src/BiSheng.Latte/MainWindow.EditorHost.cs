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
        NoteEditor.TextEditor.TextChanged += (_, _) =>
        {
            if (!_isLoadingNote)
            {
                _vm.Editor.OnContentChanged(NoteEditor.Text);
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

            RequestOutlineRefresh(deferred: true);
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
            _outlineTimer?.Stop();
            Interlocked.Increment(ref _outlineRefreshVersion);

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
            RequestOutlineRefresh(deferred: true);
        };

        _vm.NoteClosed += () => Dispatcher.Invoke(() =>
        {
            _isLoadingNote = true;
            _outlineTimer?.Stop();
            Interlocked.Increment(ref _outlineRefreshVersion);
            NoteEditor.Text = string.Empty;
            _isLoadingNote = false;
            RequestOutlineRefresh(deferred: false);
        });

        _vm.NoteList.NoteCreated += note =>
        {
            Dispatcher.Invoke(() =>
            {
                _isLoadingNote = true;
                _vm.Editor.LoadNote(note, NoteEditor.Text);
                NoteEditor.Text = _vm.Editor.EditorContent;
                _isLoadingNote = false;
                RequestOutlineRefresh(deferred: true);
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

    /// <summary>请求刷新大纲；加载大笔记时可延迟到 Background 优先级</summary>
    private void RequestOutlineRefresh(bool deferred)
    {
        if (!_vm.IsOutlinePanelVisible)
        {
            return;
        }

        if (deferred)
        {
            Dispatcher.BeginInvoke(RefreshOutline, DispatcherPriority.Background);
        }
        else
        {
            RefreshOutline();
        }
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

        // 单次快照；解析与建树在线程池完成，避免大笔记阻塞 UI
        var text = NoteEditor.Text;
        var flatItems = await Task.Run(() => OutlineViewModel.ParseFlatHeadings(text))
            .ConfigureAwait(true);

        if (version != Volatile.Read(ref _outlineRefreshVersion))
        {
            return;
        }

        _vm.Outline.ApplyHeadings(flatItems);
    }

    /// <summary>双击大纲条目跳转到编辑器对应行</summary>
    private void OnOutlineTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (OutlineTree.SelectedItem is not OutlineItem item)
        {
            return;
        }

        var editor = NoteEditor.TextEditor;
        editor.ScrollToLine(item.LineNumber + 1);

        var line = editor.Document.GetLineByNumber(item.LineNumber + 1);
        editor.CaretOffset = line.Offset;
        editor.TextArea.Caret.BringCaretToView();
    }
}

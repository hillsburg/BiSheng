using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BiSheng.Editor.Controls.MarkdownEditor.Themes;
using BiSheng.Editor.Model;
using BiSheng.Editor.Rendering;
using BiSheng.Editor.Clipboard;
using BiSheng.Editor.Parsing;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace BiSheng.Editor.Controls.MarkdownEditor
{
    public partial class MarkdownEditorControl : UserControl
    {
        private MarkdownDocumentModel? _documentModel;
        private MarkdownLineTransformer? _lineTransformer;
        private LineBackgroundRenderer? _backgroundRenderer;
        private ImageRenderer? _imageRenderer;
        private BlockElementGenerator? _blockElementGenerator;
        private HorizontalRuleRenderer? _horizontalRuleRenderer;
        private SyntaxCollapser? _syntaxCollapser;
        private HeadingMargin? _headingMargin;
        private MarkdownTheme _theme = MarkdownTheme.Light;

        // ===== 滚动条自动显隐 =====
        private ScrollViewer? _scrollViewer;
        private ScrollBar? _verticalScrollBar;
        private readonly DispatcherTimer _scrollFadeTimer;
        private bool _isMouseWheel;          // 标记当前滚动是否由鼠标滚轮触发
        private const double ScrollBarFadeDuration = 300;   // 淡入淡出动画毫秒
        private const int ScrollBarHideDelay = 1200;        // 停止滚动后隐藏延迟毫秒

        // ===== 依赖属性 =====

        public static readonly DependencyProperty ThemeProperty =
            DependencyProperty.Register(nameof(Theme), typeof(MarkdownTheme),
                typeof(MarkdownEditorControl),
                new PropertyMetadata(MarkdownTheme.Light, OnThemeChanged));

        public static readonly DependencyProperty EditorFontFamilyProperty =
            DependencyProperty.Register(nameof(EditorFontFamily), typeof(FontFamily),
                typeof(MarkdownEditorControl),
                new PropertyMetadata(new FontFamily("Segoe UI, Microsoft YaHei"), OnEditorStyleChanged));

        public static readonly DependencyProperty EditorFontSizeProperty =
            DependencyProperty.Register(nameof(EditorFontSize), typeof(double),
                typeof(MarkdownEditorControl),
                new PropertyMetadata(14.0, OnEditorStyleChanged));

        public static readonly DependencyProperty LineSpacingProperty =
            DependencyProperty.Register(nameof(LineSpacing), typeof(double),
                typeof(MarkdownEditorControl),
                new PropertyMetadata(1.4, OnEditorStyleChanged));

        public static readonly DependencyProperty DocumentPathProperty =
            DependencyProperty.Register(nameof(DocumentPath), typeof(string),
                typeof(MarkdownEditorControl),
                new PropertyMetadata(null));

        /// <summary>
        /// 图片解析器依赖属性：由宿主应用注入，用于解析 bisheng:// 等自定义 URI
        /// </summary>
        public static readonly DependencyProperty ImageResolverProperty =
            DependencyProperty.Register(nameof(ImageResolver), typeof(IImageResolver),
                typeof(MarkdownEditorControl),
                new PropertyMetadata(null, OnImageResolverChanged));

        /// <summary>
        /// ImageResolver 变更回调：重新创建 ImageRenderer 并传入新的解析器
        /// 解决时序问题——InitializeEditor 执行时 ImageResolver 可能尚未注入
        /// </summary>
        private static void OnImageResolverChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MarkdownEditorControl control && control._imageRenderer != null)
            {
                var textView = control.Editor.TextArea.TextView;

                // 移除旧的 ImageRenderer
                textView.ElementGenerators.Remove(control._imageRenderer);

                // 创建新的 ImageRenderer（带解析器）
                control._imageRenderer = new ImageRenderer(
                    control._documentModel!, control._theme,
                    (IImageResolver?)e.NewValue);
                control._imageRenderer.SetTextEditor(control.Editor);

                // 重新注册
                textView.ElementGenerators.Add(control._imageRenderer);
                textView.Redraw();
            }
        }

        public MarkdownTheme Theme
        {
            get => (MarkdownTheme)GetValue(ThemeProperty);
            set => SetValue(ThemeProperty, value);
        }

        public FontFamily EditorFontFamily
        {
            get => (FontFamily)GetValue(EditorFontFamilyProperty);
            set => SetValue(EditorFontFamilyProperty, value);
        }

        public double EditorFontSize
        {
            get => (double)GetValue(EditorFontSizeProperty);
            set => SetValue(EditorFontSizeProperty, value);
        }

        /// <summary>
        /// 行间距倍数（默认 1.4）
        /// </summary>
        public double LineSpacing
        {
            get => (double)GetValue(LineSpacingProperty);
            set => SetValue(LineSpacingProperty, value);
        }

        /// <summary>
        /// 当前文档路径，粘贴图片时会保存到同级 images 目录
        /// </summary>
        public string? DocumentPath
        {
            get => (string?)GetValue(DocumentPathProperty);
            set => SetValue(DocumentPathProperty, value);
        }

        /// <summary>
        /// 图片解析器：用于将 bisheng://img/{uuid} 等自定义 URI 解析为本地文件路径
        /// </summary>
        public IImageResolver? ImageResolver
        {
            get => (IImageResolver?)GetValue(ImageResolverProperty);
            set => SetValue(ImageResolverProperty, value);
        }

        /// <summary>
        /// 图片粘贴事件：当用户粘贴图片时触发
        /// 参数: (imageId, filePath)
        /// </summary>
        public event Action<Guid, string>? ImagePasted;

        public TextEditor TextEditor => Editor;

        public string Text
        {
            get => Editor.Text ?? string.Empty;
            set => Editor.Text = value;
        }

        public MarkdownEditorControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;

            _scrollFadeTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromMilliseconds(ScrollBarHideDelay)
            };
            _scrollFadeTimer.Tick += OnScrollFadeTimerTick;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            InitializeEditor();
            ApplyEditorStyle();
            SetupAutoHideScrollBar();
        }

        private void InitializeEditor()
        {
            _documentModel = new MarkdownDocumentModel(Editor.Document);

            // 禁用 word wrap 缩进继承：行首前导空白不应影响换行后的 TextLine 缩进，
            // 否则在行首按 space/tab 会导致所有换行 TextLine 同步缩进
            Editor.Options.InheritWordWrapIndentation = false;

            _lineTransformer = new MarkdownLineTransformer(_documentModel, _theme);
            _backgroundRenderer = new LineBackgroundRenderer(_documentModel, _theme);
            _imageRenderer = new ImageRenderer(_documentModel, _theme, ImageResolver);
            _imageRenderer.SetTextEditor(Editor);
            _blockElementGenerator = new BlockElementGenerator(_documentModel, _theme);
            _horizontalRuleRenderer = new HorizontalRuleRenderer(_documentModel, _theme);
            _syntaxCollapser = new SyntaxCollapser(_documentModel);

            var textView = Editor.TextArea.TextView;
            textView.LineSpacing = LineSpacing;
            textView.LineTransformers.Add(_lineTransformer);
            textView.BackgroundRenderers.Add(_backgroundRenderer);
            textView.ElementGenerators.Add(_imageRenderer);
            textView.ElementGenerators.Add(_blockElementGenerator);
            textView.ElementGenerators.Add(_horizontalRuleRenderer);
            textView.ElementGenerators.Add(_syntaxCollapser);

            // 左侧标题标记 Margin（H1~H6 标签），通过 LeftMargins 添加到 TextArea 左侧边栏。
            // 行号和分割虚线通过 ShowLineNumbers=True 自动添加到 RightMargins（右侧边栏），
            // 视觉布局：[HeadingMargin] [文本内容] | [分割虚线] [行号]
            _headingMargin = new HeadingMargin { LabelBrush = _theme.HeadingColor };
            Editor.TextArea.LeftMargins.Add(_headingMargin);

            Editor.TextArea.PreviewKeyDown += OnPreviewKeyDown;
            Editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
            SyncCaretToLineTransformer();
        }

        /// <summary>上次同步的光标行（0-based），用于跨行时才全量 Redraw</summary>
        private int _lastSyncedCaretLine = -1;

        /// <summary>光标移动时同步行内标记显隐（当前行保持源码可见）</summary>
        private void OnCaretPositionChanged(object? sender, EventArgs e)
        {
            SyncCaretToLineTransformer(redrawIfLineChanged: true);
        }

        /// <summary>把光标行列写入 Transformer；跨行时刷新视图以切换标记显隐</summary>
        private void SyncCaretToLineTransformer(bool redrawIfLineChanged = false)
        {
            if (_lineTransformer == null)
            {
                return;
            }

            int caretOffset = Editor.CaretOffset;
            int lineNumber = 0;
            if (Editor.Document.TextLength > 0)
            {
                var clamped = Math.Min(caretOffset, Math.Max(0, Editor.Document.TextLength));
                lineNumber = Editor.Document.GetLineByOffset(clamped).LineNumber - 1;
            }

            _lineTransformer.SetCaretPosition(lineNumber, caretOffset);

            if (redrawIfLineChanged && lineNumber != _lastSyncedCaretLine)
            {
                _lastSyncedCaretLine = lineNumber;
                Editor.TextArea.TextView.Redraw();
            }
            else if (!redrawIfLineChanged)
            {
                _lastSyncedCaretLine = lineNumber;
            }
        }

        // ===== 滚动条自动显隐实现 =====

        /// <summary>
        /// 初始化滚动条自动隐藏：查找 ScrollViewer 并挂钩滚动事件
        /// </summary>
        private void SetupAutoHideScrollBar()
        {
            _scrollViewer = Editor.Template?.FindName("PART_ScrollViewer", Editor) as ScrollViewer;
            if (_scrollViewer == null) return;

            // 初始状态：滚动条透明
            _scrollViewer.ApplyTemplate();
            _verticalScrollBar = _scrollViewer.Template?.FindName("PART_VerticalScrollBar", _scrollViewer) as ScrollBar;

            if (_verticalScrollBar != null)
            {
                _verticalScrollBar.Opacity = 0;
            }

            // 仅在鼠标滚轮滚动时显示滚动条，键盘/光标移动触发的滚动不显示
            _scrollViewer.PreviewMouseWheel += OnScrollViewerPreviewMouseWheel;
            _scrollViewer.ScrollChanged += OnScrollChanged;
            _scrollViewer.MouseLeave += OnScrollViewerMouseLeave;
        }

        /// <summary>鼠标滚轮触发时设置标记，后续 ScrollChanged 据此决定是否显示滚动条</summary>
        private void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            _isMouseWheel = true;
        }

        /// <summary>滚动发生时：仅当鼠标滚轮触发时才显示滚动条并重置计时器</summary>
        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_verticalScrollBar == null) return;

            // 如果内容不需要滚动，保持隐藏
            if (_scrollViewer!.ScrollableHeight <= 0)
            {
                AnimateScrollBarOpacity(0);
                _isMouseWheel = false;
                return;
            }

            // 仅鼠标滚轮触发的滚动才显示滚动条
            if (_isMouseWheel)
            {
                AnimateScrollBarOpacity(1);
                _scrollFadeTimer.Stop();
                _scrollFadeTimer.Start();
            }

            _isMouseWheel = false;
        }

        /// <summary>鼠标离开滚动区域时启动隐藏计时</summary>
        private void OnScrollViewerMouseLeave(object sender, MouseEventArgs e)
        {
            _scrollFadeTimer.Stop();
            _scrollFadeTimer.Start();
        }

        /// <summary>计时器到期后淡出滚动条</summary>
        private void OnScrollFadeTimerTick(object? sender, EventArgs e)
        {
            _scrollFadeTimer.Stop();
            AnimateScrollBarOpacity(0);
        }

        /// <summary>对滚动条 Opacity 执行动画</summary>
        private void AnimateScrollBarOpacity(double target)
        {
            if (_verticalScrollBar == null) return;
            var current = _verticalScrollBar.Opacity;
            if (Math.Abs(current - target) < 0.01) return;

            var animation = new DoubleAnimation(current, target,
                TimeSpan.FromMilliseconds(ScrollBarFadeDuration))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            _verticalScrollBar.BeginAnimation(UIElement.OpacityProperty, animation);
        }

        // ===== 样式应用 =====

        private static void OnEditorStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MarkdownEditorControl control && control.IsLoaded)
            {
                control.ApplyEditorStyle();
            }
        }

        private void ApplyEditorStyle()
        {
            Editor.FontFamily = EditorFontFamily;
            Editor.FontSize = EditorFontSize;

            // 行间距通过 TextView.LineSpacing 原生属性实现
            Editor.TextArea.TextView.LineSpacing = LineSpacing;

            // 更新主题字体引用
            if (_theme != null)
            {
                _theme.BaseFontFamily = EditorFontFamily;
                _theme.BaseFontSize = EditorFontSize;
            }

            Editor.TextArea.TextView.Redraw();
            if (_headingMargin != null)
            {
                _headingMargin.LabelBrush = _theme.HeadingColor;
                _headingMargin.InvalidateVisual();
            }
        }

        // ===== 键盘事件 =====

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
            {
                WrapSelection("**");
                e.Handled = true;
            }
            else if (e.Key == Key.I && Keyboard.Modifiers == ModifierKeys.Control)
            {
                WrapSelection("*");
                e.Handled = true;
            }
            else if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                InsertLink();
                e.Handled = true;
            }
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (HandlePasteImage())
                    e.Handled = true;
            }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                HandleCopy();
            }
            else if (e.Key == Key.Back && Editor.SelectionLength == 0)
            {
                if (HandleBackspaceDeleteSyntax())
                    e.Handled = true;
            }
        }

        /// <summary>
        /// Backspace 智能删除语法符号：
        /// 当光标位于块级语法前缀后方时，一次删除整个前缀
        /// </summary>
        private bool HandleBackspaceDeleteSyntax()
        {
            var doc = Editor.Document;
            int caretOffset = Editor.CaretOffset;
            if (caretOffset <= 0) return false;

            var line = doc.GetLineByOffset(caretOffset);
            string text = doc.GetText(line.Offset, line.Length);
            if (string.IsNullOrEmpty(text)) return false;

            // 光标在行内的列偏移（0-based）
            int caretColumn = caretOffset - line.Offset;

            // 分析行类型并计算前缀长度
            var lineType = LineMarkdownAnalyzer.AnalyzeLineType(text);
            int prefixLen = lineType switch
            {
                LineType.Heading1 or LineType.Heading2 or LineType.Heading3
                or LineType.Heading4 or LineType.Heading5 or LineType.Heading6
                    => LineMarkdownAnalyzer.GetHeadingPrefixLength(text),
                LineType.BulletList or LineType.OrderedList
                    => LineMarkdownAnalyzer.GetListPrefixLength(text),
                LineType.BlockQuote
                    => text.StartsWith("> ") ? 2 : (text.StartsWith(">") ? 1 : 0),
                LineType.CodeBlock
                    => text.TrimStart().StartsWith("```") ? text.Length : 0,
                LineType.HorizontalRule
                    => text.TrimEnd().Length,
                _ => 0
            };

            // 光标必须位于前缀末尾（或行尾对于分割线/代码块）
            if (prefixLen > 0 && caretColumn == prefixLen)
            {
                // 删除整个前缀
                doc.Remove(line.Offset, prefixLen);
                return true;
            }

            return false;
        }

        private void WrapSelection(string wrapper)
        {
            if (Editor.SelectionLength > 0)
            {
                var selectedText = Editor.SelectedText;
                Editor.Document.Replace(Editor.SelectionStart, Editor.SelectionLength,
                    $"{wrapper}{selectedText}{wrapper}");
            }
            else
            {
                int offset = Editor.CaretOffset;
                Editor.Document.Insert(offset, $"{wrapper}{wrapper}");
                Editor.CaretOffset = offset + wrapper.Length;
            }
        }

        private void InsertLink()
        {
            if (Editor.SelectionLength > 0)
            {
                var selectedText = Editor.SelectedText;
                int start = Editor.SelectionStart;
                Editor.Document.Replace(start, Editor.SelectionLength,
                    $"[{selectedText}](url)");
                Editor.Select(start + selectedText.Length + 3, 3);
            }
            else
            {
                int offset = Editor.CaretOffset;
                Editor.Document.Insert(offset, "[](url)");
                Editor.CaretOffset = offset + 1;
            }
        }

        // ===== 图片粘贴（UUID 命名 + bisheng:// URI + 通知宿主） =====

        /// <summary>
        /// 粘贴图片：生成 UUID → 保存到 images/{uuid}.png → 校验大小 → 插入 bisheng:// URI → 触发事件
        /// </summary>
        private bool HandlePasteImage()
        {
            if (!ClipboardHelper.HasImageInClipboard()) return false;

            // 确定保存目录：统一使用应用目录下的 images 文件夹
            string saveDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images");

            // 生成 UUID 作为文件名
            var imageId = Guid.NewGuid();
            string fileName = $"{imageId}.png";

            var imagePath = ClipboardHelper.GetImageFromClipboard(saveDir, fileName);
            if (imagePath == null) return false;

            // 校验文件大小，>10MB 则压缩（降低分辨率重编码）
            const long maxSizeBytes = 10 * 1024 * 1024;
            var fileInfo = new FileInfo(imagePath);
            if (fileInfo.Length > maxSizeBytes)
            {
                imagePath = ClipboardHelper.CompressImage(imagePath, maxSizeBytes);
            }

            // 插入 bisheng:// 自定义 URI
            string insertText = $"![image](bisheng://img/{imageId})";
            int offset = Editor.CaretOffset;
            Editor.Document.Insert(offset, insertText);
            Editor.CaretOffset = offset + insertText.Length;

            // 通知宿主应用记录图片元数据
            ImagePasted?.Invoke(imageId, imagePath);
            return true;
        }

        private void HandleCopy()
        {
            if (Editor.SelectionLength == 0) return;
            var selectedText = Editor.SelectedText;
            var html = ClipboardHelper.MarkdownToSimpleHtml(selectedText);
            ClipboardHelper.CopyAsRichText(selectedText, html);
        }

        // ===== 主题切换 =====

        private static void OnThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MarkdownEditorControl control && e.NewValue is MarkdownTheme theme)
            {
                control.ApplyTheme(theme);
            }
        }

        private void ApplyTheme(MarkdownTheme theme)
        {
            _theme = theme;
            Editor.Background = theme.BackgroundColor;
            Editor.Foreground = theme.TextColor;

            // 主题携带的正文字体同步到 AvalonEdit（换主题时必须刷新）
            if (theme.BaseFontFamily != null)
            {
                Editor.FontFamily = theme.BaseFontFamily;
                EditorFontFamily = theme.BaseFontFamily;
            }

            if (theme.BaseFontSize > 0)
            {
                Editor.FontSize = theme.BaseFontSize;
                EditorFontSize = theme.BaseFontSize;
            }

            // 光标颜色：通过 Caret.CaretBrush 设置
            var caretClr = (theme.CaretColor is SolidColorBrush caretScb) ? caretScb.Color : Colors.Black;
            Editor.TextArea.Caret.CaretBrush = new SolidColorBrush(caretClr);

            if (_documentModel != null)
            {
                var textView = Editor.TextArea.TextView;

                if (_lineTransformer != null)
                    textView.LineTransformers.Remove(_lineTransformer);
                textView.BackgroundRenderers.Remove(_backgroundRenderer!);
                textView.ElementGenerators.Remove(_imageRenderer!);
                textView.ElementGenerators.Remove(_blockElementGenerator!);
                textView.ElementGenerators.Remove(_horizontalRuleRenderer!);

                _lineTransformer = new MarkdownLineTransformer(_documentModel, _theme);
                _backgroundRenderer = new LineBackgroundRenderer(_documentModel, _theme);
                _imageRenderer = new ImageRenderer(_documentModel, _theme, ImageResolver);
                _imageRenderer.SetTextEditor(Editor);
                _blockElementGenerator = new BlockElementGenerator(_documentModel, _theme);
                _horizontalRuleRenderer = new HorizontalRuleRenderer(_documentModel, _theme);

                textView.LineTransformers.Add(_lineTransformer);
                textView.BackgroundRenderers.Add(_backgroundRenderer);
                textView.ElementGenerators.Add(_imageRenderer);
                textView.ElementGenerators.Add(_blockElementGenerator);
                textView.ElementGenerators.Add(_horizontalRuleRenderer);

                textView.LineSpacing = LineSpacing;
                SyncCaretToLineTransformer();
                textView.Redraw();
            }

            if (_headingMargin != null)
            {
                _headingMargin.LabelBrush = theme.HeadingColor;
                _headingMargin.InvalidateVisual();
            }
        }

        // ===== 文件操作 =====

        public void LoadFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                DocumentPath = filePath;
                Editor.Text = File.ReadAllText(filePath);
            }
        }

        public void SaveFile(string filePath)
        {
            DocumentPath = filePath;
            File.WriteAllText(filePath, Editor.Text);
        }
    }
}

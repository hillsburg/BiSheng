using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BiSheng.Editor.Controls.MarkdownEditor.Themes;
using BiSheng.Editor.Model;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace BiSheng.Editor.Rendering
{
    /// <summary>
    /// 图片渲染器：识别 ![alt](url) 语法，在非当前行注入图片 UI
    /// 支持本地文件、网络图片、以及 bisheng://img/{uuid} 自定义 URI Scheme。
    /// 识别结果取自文档模型中的 Image 行内标记（与 LineMarkdownAnalyzer 同一套规则：
    /// 行内代码中的语法不渲染、URL 支持一层成对括号），不在此处重复解析。
    /// 光标所在行保持源码可见，否则整段语法被图片控件替换后无法把光标移进 () 编辑地址。
    /// </summary>
    public class ImageRenderer : VisualLineElementGenerator
    {
        private readonly MarkdownDocumentModel _documentModel;
        private readonly MarkdownTheme _theme;
        private readonly IImageResolver? _imageResolver;

        /// <summary>当前光标行号（0-based），-1 表示未知；该行不渲染图片</summary>
        private int _currentLineNumber = -1;

        private readonly Dictionary<string, BitmapSource> _imageCache = new();
        private readonly HashSet<string> _loadingUrls = new();
        private TextEditor? _textEditor;

        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="documentModel">Markdown 文档模型</param>
        /// <param name="theme">主题配置</param>
        /// <param name="imageResolver">可选的图片解析器，用于处理 bisheng:// 等自定义 URI</param>
        public ImageRenderer(MarkdownDocumentModel documentModel, MarkdownTheme theme,
            IImageResolver? imageResolver = null)
        {
            _documentModel = documentModel;
            _theme = theme;
            _imageResolver = imageResolver;
        }

        /// <summary>
        /// 设置关联的 TextEditor，用于处理图片点击时移动光标
        /// </summary>
        public void SetTextEditor(TextEditor editor)
        {
            _textEditor = editor;
        }

        /// <summary>
        /// 更新光标行号（0-based）。调用方负责在跨行时重绘离开行与进入行，图片据此显隐
        /// </summary>
        public void SetCurrentLine(int lineNumber)
        {
            _currentLineNumber = lineNumber;
        }

        public override int GetFirstInterestedOffset(int startOffset)
        {
            var doc = CurrentContext.Document;
            var line = doc.GetLineByOffset(startOffset);
            int lineIndex = line.LineNumber - 1;
            if (lineIndex == _currentLineNumber)
            {
                return -1;
            }

            var text = doc.GetText(line.Offset, line.Length);
            int relativeOffset = startOffset - line.Offset;

            // 代码块行的标记已被文档模型清空，这里天然不会命中
            int first = -1;
            foreach (var marker in GetImageMarkers(lineIndex))
            {
                if (marker.SyntaxStartIndex < relativeOffset)
                {
                    continue;
                }

                if ((first < 0 || marker.SyntaxStartIndex < first)
                    && TryReadImage(text, marker, out _, out _))
                {
                    first = marker.SyntaxStartIndex;
                }
            }

            return first < 0 ? -1 : line.Offset + first;
        }

        public override VisualLineElement? ConstructElement(int offset)
        {
            var doc = CurrentContext.Document;
            var line = doc.GetLineByOffset(offset);
            int lineIndex = line.LineNumber - 1;
            var text = doc.GetText(line.Offset, line.Length);
            int relativeOffset = offset - line.Offset;

            var marker = GetImageMarkers(lineIndex)
                .FirstOrDefault(m => m.SyntaxStartIndex == relativeOffset);
            if (marker == null || !TryReadImage(text, marker, out var altText, out var url))
            {
                return null;
            }

            int elementLength = marker.SyntaxEndIndex - marker.SyntaxStartIndex + 1;

            // 记录图片在文档中的偏移，用于点击时定位光标
            var imageControl = CreateImageControl(url, altText, offset, elementLength);
            return new InlineObjectElement(elementLength, imageControl);
        }

        private IEnumerable<InlineMarker> GetImageMarkers(int lineIndex)
        {
            var markers = _documentModel.GetLineState(lineIndex)?.InlineMarkers;
            if (markers == null)
            {
                return Enumerable.Empty<InlineMarker>();
            }

            return markers.Where(m => m.Style == InlineStyle.Image);
        }

        /// <summary>
        /// 从 Image 标记覆盖的 ![alt](url) 中取出 alt 与 url。
        /// url 为空白时返回 false：刚输入 ![]( ) 还没填地址，不应渲染成「图片不存在」占位
        /// </summary>
        private static bool TryReadImage(string text, InlineMarker marker, out string altText, out string url)
        {
            altText = string.Empty;
            url = string.Empty;

            int start = marker.SyntaxStartIndex;
            int end = marker.SyntaxEndIndex;
            if (start < 0 || end >= text.Length || end - start < 4)
            {
                return false;
            }

            // alt 不含 ']'（与解析器正则一致），第一个 ']' 即 alt 的结束
            int closeBracket = text.IndexOf(']', start + 2);
            if (closeBracket < 0 || closeBracket + 1 >= end || text[closeBracket + 1] != '(')
            {
                return false;
            }

            altText = text.Substring(start + 2, closeBracket - start - 2);
            url = text.Substring(closeBracket + 2, end - closeBracket - 2).Trim();
            return url.Length > 0;
        }

        private UIElement CreateImageControl(string url, string altText, int docOffset, int elementLength)
        {
            // 1. 缓存命中 → 直接渲染
            if (_imageCache.TryGetValue(url, out var cachedBitmap))
            {
                return CreateImageBorder(cachedBitmap, docOffset, elementLength);
            }

            // 2. HTTP/HTTPS 网络图片
            if (IsHttpUrl(url))
            {
                if (!_loadingUrls.Contains(url))
                {
                    _loadingUrls.Add(url);
                    LoadNetworkImageAsync(url);
                }

                return CreatePlaceholderControl(altText, url, "加载中...", docOffset, elementLength);
            }

            // 3. bisheng:// 自定义 URI → 通过 IImageResolver 解析
            if (IsBiShengUri(url))
            {
                return CreateBiShengImageControl(url, altText, docOffset, elementLength);
            }

            // 4. 本地文件路径
            if (IsLocalPath(url) && File.Exists(url))
            {
                try
                {
                    var bitmap = LoadLocalImage(url);
                    _imageCache[url] = bitmap;
                    return CreateImageBorder(bitmap, docOffset, elementLength);
                }
                catch
                {
                    return CreatePlaceholderControl(altText, url, "无法加载图片", docOffset, elementLength);
                }
            }

            // 5. 未知 / 不存在
            return CreatePlaceholderControl(altText, url, "图片不存在", docOffset, elementLength);
        }

        // ==========================================================
        //  bisheng:// 自定义 URI 处理
        // ==========================================================

        /// <summary>
        /// 处理 bisheng://img/{uuid} 格式的图片 URI
        /// </summary>
        private UIElement CreateBiShengImageControl(string uri, string altText,
            int docOffset, int elementLength)
        {
            if (_imageResolver == null)
            {
                return CreatePlaceholderControl(altText, uri,
                    "图片解析器未配置", docOffset, elementLength);
            }

            // 尝试解析为本地路径
            var localPath = _imageResolver.ResolveLocalPath(uri);
            if (localPath != null && File.Exists(localPath))
            {
                try
                {
                    var bitmap = LoadLocalImage(localPath);
                    _imageCache[uri] = bitmap;
                    return CreateImageBorder(bitmap, docOffset, elementLength);
                }
                catch
                {
                    // 文件存在但加载失败，标记为不可用
                }
            }

            // 检查状态
            var status = _imageResolver.GetStatus(uri);
            switch (status)
            {
                case ImageResolveStatus.Loading:
                    return CreatePlaceholderControl(altText, uri,
                        "下载中...", docOffset, elementLength);

                case ImageResolveStatus.Unavailable:
                    // 触发下载请求
                    _imageResolver.RequestDownload(uri);
                    return CreatePlaceholderWithRetry(altText, uri,
                        "图片未同步", docOffset, elementLength);

                default:
                    return CreatePlaceholderControl(altText, uri,
                        "图片不可用", docOffset, elementLength);
            }
        }

        /// <summary>
        /// 判断是否为 bisheng:// 自定义 URI
        /// </summary>
        private static bool IsBiShengUri(string url)
        {
            return url.StartsWith("bisheng://img/");
        }

        /// <summary>
        /// 从 bisheng:// URI 中提取 UUID
        /// </summary>
        public static string? ExtractImageId(string uri)
        {
            const string prefix = "bisheng://img/";
            if (!uri.StartsWith(prefix)) return null;
            var id = uri.Substring(prefix.Length);
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }

        // ==========================================================
        //  占位符 UI
        // ==========================================================

        private Border CreateImageBorder(BitmapSource bitmap, int docOffset, int elementLength)
        {
            // 获取编辑器可用宽度（减去左右边距）
            double availableWidth = (_textEditor?.ActualWidth ?? 600) - 40;

            // 图片原始宽度（DPI 无关，WPF 中 PixelWidth 即为逻辑像素）
            double imgWidth = bitmap.PixelWidth;

            // 小图原样展示，大图缩放到可用宽度
            double displayMaxWidth = imgWidth <= availableWidth ? imgWidth : availableWidth;

            var image = new System.Windows.Controls.Image
            {
                Source = bitmap,
                MaxWidth = displayMaxWidth,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(4, 4, 4, 4),
                Cursor = Cursors.Hand
            };

            var border = new Border
            {
                Child = image,
                BorderBrush = _theme.CodeBlockBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(4, 2, 4, 2),
                Cursor = Cursors.Hand
            };

            // 单击图片时将光标移到图片语法位置，这样切回该行就能显示原始文本
            AttachClickToFocus(border, docOffset, elementLength);

            return border;
        }

        private UIElement CreatePlaceholderControl(string altText, string url, string hint,
            int docOffset, int elementLength)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical };
            stack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(altText) ? url : altText,
                Foreground = _theme.LinkColor,
                FontStyle = FontStyles.Italic,
                FontSize = _theme.BaseFontSize * 0.9,
                Margin = new Thickness(4, 2, 4, 0)
            });
            stack.Children.Add(new TextBlock
            {
                Text = hint,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                FontSize = _theme.BaseFontSize * 0.75,
                Margin = new Thickness(4, 0, 4, 2)
            });

            var border = new Border
            {
                Child = stack,
                Background = _theme.InlineCodeBackground,
                BorderBrush = _theme.CodeBlockBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(4, 2, 4, 2),
                Cursor = Cursors.Hand
            };

            AttachClickToFocus(border, docOffset, elementLength);

            return border;
        }

        /// <summary>
        /// 创建带重试按钮的占位符（用于 bisheng:// URI 图片未同步时）
        /// </summary>
        private UIElement CreatePlaceholderWithRetry(string altText, string uri, string hint,
            int docOffset, int elementLength)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical };
            stack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(altText) ? uri : altText,
                Foreground = _theme.LinkColor,
                FontStyle = FontStyles.Italic,
                FontSize = _theme.BaseFontSize * 0.9,
                Margin = new Thickness(4, 2, 4, 0)
            });
            stack.Children.Add(new TextBlock
            {
                Text = hint,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                FontSize = _theme.BaseFontSize * 0.75,
                Margin = new Thickness(4, 0, 4, 2)
            });

            // 重试按钮
            var retryBtn = new Button
            {
                Content = "重新加载",
                FontSize = _theme.BaseFontSize * 0.75,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(4, 2, 4, 2),
                Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            retryBtn.Click += (s, e) =>
            {
                _imageResolver?.RequestDownload(uri);
                e.Handled = true;
            };
            stack.Children.Add(retryBtn);

            var border = new Border
            {
                Child = stack,
                Background = _theme.InlineCodeBackground,
                BorderBrush = _theme.CodeBlockBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(4, 2, 4, 2),
                Cursor = Cursors.Hand
            };

            AttachClickToFocus(border, docOffset, elementLength);

            return border;
        }

        /// <summary>
        /// 给 UI 元素附加点击事件：单击时将光标定位到图片语法之后
        /// </summary>
        private void AttachClickToFocus(UIElement element, int docOffset, int elementLength)
        {
            element.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (_textEditor == null) return;

                // 将光标放置到图片语法结束之后
                int afterImage = docOffset + elementLength;
                int safeOffset = Math.Min(afterImage, _textEditor.Document.TextLength);
                _textEditor.CaretOffset = safeOffset;
                _textEditor.Focus();
                e.Handled = true;
            };
        }

        // ==========================================================
        //  图片加载
        // ==========================================================

        private BitmapSource LoadLocalImage(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            // 不设置 DecodePixelWidth，保留原始尺寸以便判断是否需要缩放
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private async void LoadNetworkImageAsync(string url)
        {
            try
            {
                var bytes = await _httpClient.GetByteArrayAsync(url);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new MemoryStream(bytes);
                bitmap.EndInit();
                bitmap.Freeze();

                _imageCache[url] = bitmap;
                _loadingUrls.Remove(url);

                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (Window window in Application.Current.Windows)
                    {
                        if (window.IsLoaded)
                        {
                            foreach (var editor in FindVisualChildren<TextEditor>(window))
                            {
                                editor.TextArea.TextView.Redraw();
                            }
                        }
                    }
                }), DispatcherPriority.Background);
            }
            catch
            {
                _loadingUrls.Remove(url);
            }
        }

        // ==========================================================
        //  URL 判断辅助方法
        // ==========================================================

        private static bool IsLocalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.StartsWith("http://") || path.StartsWith("https://")) return false;
            if (IsBiShengUri(path)) return false;
            return true;
        }

        private static bool IsHttpUrl(string url)
        {
            return url.StartsWith("http://") || url.StartsWith("https://");
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) yield return match;
                foreach (var descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }
    }
}

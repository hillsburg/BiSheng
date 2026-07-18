using System.Windows;
using System.Windows.Media;
using BiSheng.Editor.Controls.MarkdownEditor.Themes;
using BiSheng.Editor.Model;
using BiSheng.Editor.Parsing;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace BiSheng.Editor.Rendering
{
    /// <summary>
    /// AvalonEdit 行级即时渲染 Transformer
    /// 块级语法由 SyntaxCollapser 零宽度折叠，本 Transformer 负责：
    /// - 标题/引用内容样式（字号、颜色、字体）
    /// - 列表前缀透明隐藏但保留字号占位（供 Bullet / 序号完整绘制）
    /// - 行内语法标记隐藏与内容样式
    ///
    /// 行内标记隐藏策略（便于输入）：
    /// - 成对标记中间无内容时不隐藏（如 ****、**）
    /// - 光标所在行不隐藏行内标记（源码可见）
    /// - 光标落在某标记语法区间内时，该标记不隐藏
    /// </summary>
    public class MarkdownLineTransformer : DocumentColorizingTransformer
    {
        private readonly MarkdownDocumentModel _documentModel;
        private readonly MarkdownTheme _theme;

        /// <summary>当前光标行号（0-based），-1 表示未知</summary>
        private int _currentLineNumber = -1;

        /// <summary>当前光标文档偏移</summary>
        private int _caretOffset = -1;

        public MarkdownLineTransformer(MarkdownDocumentModel documentModel, MarkdownTheme theme)
        {
            _documentModel = documentModel;
            _theme = theme;
        }

        /// <summary>
        /// 更新光标位置（行号 0-based + 文档偏移），供行内标记显隐决策
        /// </summary>
        public void SetCaretPosition(int lineNumber, int caretOffset)
        {
            _currentLineNumber = lineNumber;
            _caretOffset = caretOffset;
        }

        /// <summary>兼容旧调用：仅更新行号</summary>
        public void SetCurrentLine(int lineNumber)
        {
            _currentLineNumber = lineNumber;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            int lineIndex = line.LineNumber - 1; // 0-based

            var state = _documentModel.GetLineState(lineIndex);
            if (state == null) return;

            var text = CurrentContext.Document.GetText(line.Offset, line.Length);
            if (string.IsNullOrEmpty(text)) return;

            // 根据行类型应用块级样式
            ApplyBlockStyle(line, state, text);

            // 应用行内样式
            ApplyInlineStyles(line, state, text, lineIndex);
        }

        private void ApplyBlockStyle(DocumentLine line, LineState state, string text)
        {
            int lineStart = line.Offset;
            int lineEnd = line.Offset + line.Length;

            switch (state.Type)
            {
                case LineType.Heading1:
                case LineType.Heading2:
                case LineType.Heading3:
                case LineType.Heading4:
                case LineType.Heading5:
                case LineType.Heading6:
                    ApplyHeadingStyle(line, state, text);
                    break;

                case LineType.BulletList:
                case LineType.OrderedList:
                    ApplyListStyle(line, text);
                    break;

                case LineType.BlockQuote:
                    ApplyQuoteStyle(line, text);
                    break;

                case LineType.CodeBlock:
                    ApplyCodeFenceStyle(line);
                    break;

                case LineType.CodeBlockContent:
                    ApplyCodeContentStyle(line);
                    break;

                case LineType.HorizontalRule:
                    ApplyHorizontalRuleStyle(line);
                    break;
            }
        }

        private void ApplyHeadingStyle(DocumentLine line, LineState state, string text)
        {
            int prefixLen = LineMarkdownAnalyzer.GetHeadingPrefixLength(text);
            if (prefixLen == 0) return;

            var fontSize = state.Type switch
            {
                LineType.Heading1 => _theme.Heading1FontSize,
                LineType.Heading2 => _theme.Heading2FontSize,
                LineType.Heading3 => _theme.Heading3FontSize,
                LineType.Heading4 => _theme.Heading4FontSize,
                LineType.Heading5 => _theme.Heading5FontSize,
                LineType.Heading6 => _theme.Heading6FontSize,
                _ => _theme.BaseFontSize
            };

            int lineStart = line.Offset;

            // 行首缩进 + # 前缀由 SyntaxCollapser 零宽度折叠（视觉顶格），此处只设置内容样式
            ChangeLinePart(lineStart + prefixLen, lineStart + line.Length, element =>
            {
                element.TextRunProperties.SetForegroundBrush(_theme.HeadingColor);
                element.TextRunProperties.SetFontRenderingEmSize(fontSize);
                element.TextRunProperties.SetFontHintingEmSize(fontSize);
                element.TextRunProperties.SetTypeface(new Typeface(
                    _theme.BaseFontFamily,
                    FontStyles.Normal,
                    FontWeights.Bold,
                    FontStretches.Normal));
            });
        }

        private void ApplyListStyle(DocumentLine line, string text)
        {
            int prefixLen = LineMarkdownAnalyzer.GetListPrefixLength(text);
            if (prefixLen == 0)
            {
                return;
            }

            int lineStart = line.Offset;

            // 仅透明隐藏前缀，保留原始字号占位，供 Bullet/序号绘制且不被裁切
            ConcealPreserveWidth(lineStart, lineStart + prefixLen);
        }

        private void ApplyQuoteStyle(DocumentLine line, string text)
        {
            int lineStart = line.Offset;
            int quotePrefixLen = text.StartsWith("> ") ? 2 : (text.StartsWith(">") ? 1 : 0);

            // 给整行添加引用样式
            ChangeLinePart(lineStart, lineStart + line.Length, element =>
            {
                element.TextRunProperties.SetForegroundBrush(_theme.QuoteTextColor);
                element.TextRunProperties.SetTypeface(new Typeface(
                    _theme.BaseFontFamily,
                    FontStyles.Italic,
                    FontWeights.Normal,
                    FontStretches.Normal));
            });

            // > 前缀由 SyntaxCollapser 零宽度折叠，此处只设置内容样式
        }

        private void ApplyCodeFenceStyle(DocumentLine line)
        {
            // 整行由 SyntaxCollapser 零宽度折叠，无需额外处理
        }

        private void ApplyCodeContentStyle(DocumentLine line)
        {
            int lineStart = line.Offset;
            ChangeLinePart(lineStart, lineStart + line.Length, element =>
            {
                element.TextRunProperties.SetForegroundBrush(
                    new SolidColorBrush(Color.FromRgb(180, 180, 180)));
                element.TextRunProperties.SetTypeface(new Typeface(
                    _theme.CodeFontFamily,
                    FontStyles.Normal,
                    FontWeights.Normal,
                    FontStretches.Normal));
            });
        }

        private void ApplyHorizontalRuleStyle(DocumentLine line)
        {
            // 整行由 SyntaxCollapser 零宽度折叠，无需额外处理
        }

        private void ApplyInlineStyles(DocumentLine line, LineState state, string text, int lineIndex)
        {
            if (state.InlineMarkers == null || state.InlineMarkers.Count == 0)
            {
                return;
            }

            if (state.Type == LineType.CodeBlockContent || state.Type == LineType.CodeBlock)
            {
                return;
            }

            int lineStart = line.Offset;

            // 光标所在行：保留行内源码标记，避免成对符号被藏后无法继续输入
            bool isCaretLine = lineIndex == _currentLineNumber;

            foreach (var marker in state.InlineMarkers)
            {
                int absSyntaxStart = lineStart + marker.SyntaxStartIndex;
                int absContentStart = lineStart + marker.StartIndex;
                int absContentEnd = lineStart + marker.EndIndex + 1;
                int absSyntaxEnd = lineStart + marker.SyntaxEndIndex + 1;

                // 确保不越界
                absSyntaxEnd = Math.Min(absSyntaxEnd, lineStart + line.Length);
                absContentEnd = Math.Min(absContentEnd, lineStart + line.Length);
                absContentStart = Math.Min(absContentStart, absSyntaxEnd);

                bool hasContent = marker.StartIndex <= marker.EndIndex
                    && absContentStart < absContentEnd;

                // 光标落在该标记整段语法内（含两侧符号）时也不隐藏
                bool caretInsideMarker = _caretOffset >= absSyntaxStart
                    && _caretOffset <= absSyntaxEnd;

                bool concealMarkers = hasContent
                    && !isCaretLine
                    && !caretInsideMarker;

                switch (marker.Style)
                {
                    case InlineStyle.Bold:
                        if (concealMarkers)
                        {
                            HideSyntax(absSyntaxStart, absContentStart);
                            HideSyntax(absContentEnd, absSyntaxEnd);
                        }

                        if (hasContent)
                        {
                            ChangeLinePart(absContentStart, absContentEnd, element =>
                            {
                                var oldTypeface = element.TextRunProperties.Typeface;
                                element.TextRunProperties.SetTypeface(new Typeface(
                                    oldTypeface.FontFamily,
                                    oldTypeface.Style,
                                    FontWeights.Bold,
                                    oldTypeface.Stretch));
                            });
                        }

                        break;

                    case InlineStyle.Italic:
                        if (concealMarkers)
                        {
                            HideSyntax(absSyntaxStart, absContentStart);
                            HideSyntax(absContentEnd, absSyntaxEnd);
                        }

                        if (hasContent)
                        {
                            ChangeLinePart(absContentStart, absContentEnd, element =>
                            {
                                var oldTypeface = element.TextRunProperties.Typeface;
                                element.TextRunProperties.SetTypeface(new Typeface(
                                    oldTypeface.FontFamily,
                                    FontStyles.Italic,
                                    oldTypeface.Weight,
                                    oldTypeface.Stretch));
                            });
                        }

                        break;

                    case InlineStyle.InlineCode:
                        if (concealMarkers)
                        {
                            HideSyntax(absSyntaxStart, absContentStart);
                            HideSyntax(absContentEnd, absSyntaxEnd);
                        }

                        if (hasContent)
                        {
                            ChangeLinePart(absContentStart, absContentEnd, element =>
                            {
                                element.TextRunProperties.SetForegroundBrush(_theme.InlineCodeForeground);
                                element.TextRunProperties.SetTypeface(new Typeface(
                                    _theme.CodeFontFamily,
                                    FontStyles.Normal,
                                    FontWeights.Normal,
                                    FontStretches.Normal));
                            });
                        }

                        break;

                    case InlineStyle.Link:
                        if (concealMarkers)
                        {
                            // 隐藏 [ 和 ](url)
                            HideSyntax(absSyntaxStart, absSyntaxStart + 1);
                            if (absContentEnd <= lineStart + line.Length && absSyntaxEnd <= lineStart + line.Length)
                            {
                                HideSyntax(absContentEnd, absSyntaxEnd);
                            }
                        }

                        if (hasContent)
                        {
                            ChangeLinePart(absContentStart, absContentEnd, element =>
                            {
                                element.TextRunProperties.SetForegroundBrush(_theme.LinkColor);
                                element.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
                            });
                        }

                        break;
                }
            }
        }

        /// <summary>
        /// 隐藏语法符号：透明前景色 + 极小字号（零宽度，不占视觉空间）
        /// </summary>
        private void HideSyntax(int start, int end)
        {
            if (start >= end)
            {
                return;
            }

            var doc = CurrentContext.Document;
            end = Math.Min(end, doc.TextLength);
            start = Math.Min(start, end);
            if (start >= end)
            {
                return;
            }

            ChangeLinePart(start, end, element =>
            {
                element.TextRunProperties.SetForegroundBrush(Brushes.Transparent);
                element.TextRunProperties.SetFontHintingEmSize(0.1);
            });
        }

        /// <summary>
        /// 隐藏列表等前缀但仍保留字号宽度，避免自定义标记画到可视区外被裁切
        /// </summary>
        private void ConcealPreserveWidth(int start, int end)
        {
            if (start >= end)
            {
                return;
            }

            var doc = CurrentContext.Document;
            end = Math.Min(end, doc.TextLength);
            start = Math.Min(start, end);
            if (start >= end)
            {
                return;
            }

            ChangeLinePart(start, end, element =>
            {
                element.TextRunProperties.SetForegroundBrush(Brushes.Transparent);
            });
        }
    }
}

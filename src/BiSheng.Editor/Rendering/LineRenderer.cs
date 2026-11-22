using System.Globalization;
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
    /// 行背景渲染器：代码块/引用块背景、水平分割线、列表 Bullet / 序号 / 任务勾选框绘制
    /// </summary>
    public class LineBackgroundRenderer : IBackgroundRenderer
    {
        private readonly MarkdownDocumentModel _documentModel;
        private readonly MarkdownTheme _theme;
        private int _currentLineNumber = -1;

        /// <summary>列表标记与正文左缘的水平间隙（像素）</summary>
        private const double ListMarkerContentGap = 6;

        /// <summary>无序列表 Bullet 相对正文字号的倍率上限</summary>
        private const double BulletFontScale = 1.15;

        /// <summary>勾选框边长相对正文字号的倍率</summary>
        private const double TaskBoxFontScale = 0.9;

        /// <summary>勾选框中心距首行基线的高度（以字号计），使方框与正文字形垂直居中</summary>
        private const double TaskBoxCenterAboveBaseline = 0.36;

        public LineBackgroundRenderer(MarkdownDocumentModel documentModel, MarkdownTheme theme)
        {
            _documentModel = documentModel;
            _theme = theme;
        }

        public KnownLayer Layer => KnownLayer.Background;

        public void SetCurrentLine(int lineNumber)
        {
            _currentLineNumber = lineNumber;
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (textView.VisualLines.Count == 0)
            {
                return;
            }

            foreach (var visualLine in textView.VisualLines)
            {
                if (visualLine.IsDisposed)
                {
                    continue;
                }

                var firstLine = visualLine.FirstDocumentLine;
                int lineIndex = firstLine.LineNumber - 1;

                var state = _documentModel.GetLineState(lineIndex);
                if (state == null)
                {
                    continue;
                }

                var lineTop = visualLine.GetTextLineVisualYPosition(
                    visualLine.TextLines[0], VisualYPosition.TextTop);
                var lineBottom = visualLine.GetTextLineVisualYPosition(
                    visualLine.TextLines[visualLine.TextLines.Count - 1], VisualYPosition.TextBottom);

                var rect = new Rect(0, lineTop - textView.ScrollOffset.Y,
                    textView.ActualWidth, lineBottom - lineTop);

                switch (state.Type)
                {
                    case LineType.CodeBlock:
                    case LineType.CodeBlockContent:
                        DrawCodeBlockBackground(drawingContext, rect, state);
                        break;

                    case LineType.BlockQuote:
                        DrawQuoteBackground(drawingContext, rect, lineTop, lineBottom, textView);
                        break;

                    case LineType.HorizontalRule:
                        DrawHorizontalRule(drawingContext, rect);
                        break;

                    case LineType.BulletList:
                        DrawBulletMarker(drawingContext, visualLine, firstLine,
                            textView, lineTop);
                        break;

                    case LineType.OrderedList:
                        DrawOrderedMarker(drawingContext, visualLine, firstLine,
                            textView, lineTop, state.RawText);
                        break;
                }
            }
        }

        private void DrawCodeBlockBackground(DrawingContext dc, Rect rect, LineState state)
        {
            var brush = _theme.CodeBlockBackground;
            dc.DrawRectangle(brush, null, rect);

            var pen = new Pen(_theme.CodeBlockBorder, 1);
            if (state.Type == LineType.CodeBlock)
            {
                if (state.BlockEndLine < 0)
                {
                    dc.DrawLine(pen, rect.TopLeft, rect.TopRight);
                }
                else
                {
                    dc.DrawLine(pen, rect.BottomLeft, rect.BottomRight);
                }
            }
        }

        private void DrawQuoteBackground(DrawingContext dc, Rect rect,
            double lineTop, double lineBottom, TextView textView)
        {
            var leftBorderPen = new Pen(_theme.QuoteBorderColor, 3);
            double x = 16;
            dc.DrawLine(leftBorderPen,
                new Point(x, lineTop - textView.ScrollOffset.Y),
                new Point(x, lineBottom - textView.ScrollOffset.Y));

            var bgRect = new Rect(0, rect.Top, textView.ActualWidth, rect.Height);
            dc.DrawRectangle(_theme.QuoteBackground, null, bgRect);
        }

        private void DrawHorizontalRule(DrawingContext dc, Rect rect)
        {
            double y = rect.Top + rect.Height / 2;
            var pen = new Pen(_theme.HorizontalRuleColor, 1);
            dc.DrawLine(pen,
                new Point(20, y),
                new Point(rect.Width - 20, y));
        }

        /// <summary>
        /// 在列表前缀占位内绘制 Bullet（•）。
        /// 多行折行时标记对齐首行；水平方向限制在前缀/缩进可用区内。
        /// </summary>
        private void DrawBulletMarker(DrawingContext dc, VisualLine visualLine,
            DocumentLine docLine, TextView textView,
            double lineTop)
        {
            var text = textView.Document.GetText(docLine.Offset, docLine.Length);
            if (!LineMarkdownAnalyzer.TryGetListPrefixInfo(text, out var info) || info.IsOrdered)
            {
                return;
            }

            if (info.IsTask)
            {
                DrawTaskBox(dc, textView, visualLine);
                return;
            }

            DrawListMarker(
                dc,
                visualLine,
                textView,
                lineTop,
                info,
                markerText: "•",
                fontWeight: FontWeights.Bold,
                preferredFontSize: _theme.BaseFontSize * BulletFontScale);
        }

        /// <summary>
        /// 绘制有序列表序号；前缀长度与分析器一致（含尾随空白）。
        /// </summary>
        private void DrawOrderedMarker(DrawingContext dc, VisualLine visualLine,
            DocumentLine docLine, TextView textView,
            double lineTop, string rawText)
        {
            if (!LineMarkdownAnalyzer.TryGetListPrefixInfo(rawText, out var info)
                || !info.IsOrdered
                || string.IsNullOrEmpty(info.OrderedNumber))
            {
                return;
            }

            // 与 GitHub 一致：任务项用勾选框取代序号
            if (info.IsTask)
            {
                DrawTaskBox(dc, textView, visualLine);
                return;
            }

            DrawListMarker(
                dc,
                visualLine,
                textView,
                lineTop,
                info,
                markerText: $"{info.OrderedNumber}.",
                fontWeight: FontWeights.Normal,
                preferredFontSize: _theme.BaseFontSize);
        }

        /// <summary>
        /// 计算任务项勾选框在 TextView 视口中的矩形。绘制与点击命中共用此方法，保证画的位置就是能点的位置。
        /// 方框在前缀占位内右对齐（与 Bullet 相同），垂直方向按首行基线对齐正文字形中心。
        /// </summary>
        /// <param name="textView">所属文本视图</param>
        /// <param name="visualLine">待计算的视觉行</param>
        /// <param name="box">勾选框矩形（视口坐标）</param>
        /// <param name="info">该行列表前缀信息，含勾选框位置与状态</param>
        /// <returns>该行是带勾选框的任务项且几何可用时返回 true</returns>
        public bool TryGetTaskBoxRect(TextView textView, VisualLine visualLine, out Rect box, out ListPrefixInfo info)
        {
            box = Rect.Empty;
            var docLine = visualLine.FirstDocumentLine;
            var text = textView.Document.GetText(docLine.Offset, docLine.Length);
            if (!LineMarkdownAnalyzer.TryGetListPrefixInfo(text, out info) || !info.IsTask)
            {
                return false;
            }

            // 代码块内的 "- [ ]" 是字面量，不能当成任务项
            var state = _documentModel.GetLineState(docLine.LineNumber - 1);
            if (state == null || state.Type is not (LineType.BulletList or LineType.OrderedList))
            {
                return false;
            }

            if (!TryGetPrefixVisualColumns(
                    visualLine,
                    info.IndentLength,
                    info.PrefixLength,
                    out var markerStartColumn,
                    out var prefixEndColumn))
            {
                return false;
            }

            var firstTextLine = visualLine.TextLines[0];
            double lineStartX = visualLine.GetTextLineVisualXPosition(firstTextLine, 0);
            double markerSlotStartX = visualLine.GetTextLineVisualXPosition(firstTextLine, markerStartColumn);
            double contentX = visualLine.GetTextLineVisualXPosition(firstTextLine, prefixEndColumn);

            // 占位不够宽时缩小方框，不越过正文左缘
            double side = Math.Min(
                _theme.BaseFontSize * TaskBoxFontScale,
                contentX - ListMarkerContentGap - lineStartX);
            if (side <= 2)
            {
                return false;
            }

            double lineTop = visualLine.GetTextLineVisualYPosition(firstTextLine, VisualYPosition.TextTop)
                             - textView.ScrollOffset.Y;
            double centerY = lineTop + firstTextLine.Baseline - _theme.BaseFontSize * TaskBoxCenterAboveBaseline;
            double x = PlaceMarkerX(lineStartX, markerSlotStartX, contentX, side);
            box = new Rect(x, centerY - side / 2, side, side);
            return true;
        }

        /// <summary>绘制勾选框：未勾选为描边方框，已勾选为强调色填充加白色对勾</summary>
        private void DrawTaskBox(DrawingContext dc, TextView textView, VisualLine visualLine)
        {
            if (!TryGetTaskBoxRect(textView, visualLine, out var box, out var info))
            {
                return;
            }

            double radius = box.Width * 0.18;
            if (!info.IsChecked)
            {
                var border = new Pen(_theme.BulletColor, 1.2);
                var inset = new Rect(box.X + 0.6, box.Y + 0.6, box.Width - 1.2, box.Height - 1.2);
                dc.DrawRoundedRectangle(null, border, inset, radius, radius);
                return;
            }

            dc.DrawRoundedRectangle(_theme.LinkColor, null, box, radius, radius);

            // 对勾三点按方框比例取，任意字号下形状一致
            var check = new StreamGeometry();
            using (var ctx = check.Open())
            {
                ctx.BeginFigure(new Point(box.X + box.Width * 0.24, box.Y + box.Height * 0.52), false, false);
                ctx.LineTo(new Point(box.X + box.Width * 0.43, box.Y + box.Height * 0.71), true, false);
                ctx.LineTo(new Point(box.X + box.Width * 0.77, box.Y + box.Height * 0.31), true, false);
            }

            check.Freeze();
            var checkPen = new Pen(Brushes.White, Math.Max(1.4, box.Width * 0.13))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            dc.DrawGeometry(null, checkPen, check);
        }

        /// <summary>按统一前缀信息绘制列表标记</summary>
        private void DrawListMarker(
            DrawingContext dc,
            VisualLine visualLine,
            TextView textView,
            double lineTop,
            ListPrefixInfo info,
            string markerText,
            FontWeight fontWeight,
            double preferredFontSize)
        {
            var firstTextLine = visualLine.TextLines[0];
            double yTop = lineTop - textView.ScrollOffset.Y;
            double pixelsPerDip = VisualTreeHelper.GetDpi(textView).PixelsPerDip;

            if (!TryGetPrefixVisualColumns(
                    visualLine,
                    info.IndentLength,
                    info.PrefixLength,
                    out var markerStartColumn,
                    out var prefixEndColumn))
            {
                return;
            }

            // 左缘可用到行首（含缩进），槽不够宽时可略侵入缩进区
            double lineStartX = visualLine.GetTextLineVisualXPosition(firstTextLine, 0);
            double markerSlotStartX = visualLine.GetTextLineVisualXPosition(
                firstTextLine, markerStartColumn);
            double contentX = visualLine.GetTextLineVisualXPosition(
                firstTextLine, prefixEndColumn);

            var typeface = new Typeface(
                _theme.BaseFontFamily,
                FontStyles.Normal,
                fontWeight,
                FontStretches.Normal);

            var ft = CreateFittedMarkerText(
                markerText,
                typeface,
                preferredFontSize,
                contentX - ListMarkerContentGap - lineStartX,
                pixelsPerDip);

            double markerX = PlaceMarkerX(
                lineStartX,
                markerSlotStartX,
                contentX,
                ft.WidthIncludingTrailingWhitespace);
            double markerY = AlignMarkerToFirstLineBaseline(
                yTop, firstTextLine.Baseline, ft.Baseline);
            dc.DrawText(ft, new Point(markerX, markerY));
        }

        /// <summary>
        /// 生成不超过可用宽度的标记 FormattedText；过宽则等比缩小字号
        /// </summary>
        private FormattedText CreateFittedMarkerText(
            string markerText,
            Typeface typeface,
            double preferredFontSize,
            double maxWidth,
            double pixelsPerDip)
        {
            double fontSize = preferredFontSize;
            FormattedText ft = CreateMarkerText(markerText, typeface, fontSize, pixelsPerDip);

            if (maxWidth <= 1)
            {
                return ft;
            }

            // 多位数序号等可能宽于前缀槽，缩到能放入 [行首, 正文 - gap]
            int guard = 0;
            while (ft.WidthIncludingTrailingWhitespace > maxWidth
                   && fontSize > preferredFontSize * 0.55
                   && guard++ < 8)
            {
                fontSize *= 0.85;
                ft = CreateMarkerText(markerText, typeface, fontSize, pixelsPerDip);
            }

            return ft;
        }

        /// <summary>创建标记用 FormattedText</summary>
        private FormattedText CreateMarkerText(
            string markerText,
            Typeface typeface,
            double fontSize,
            double pixelsPerDip)
        {
            return new FormattedText(
                markerText,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                _theme.BulletColor,
                pixelsPerDip);
        }

        /// <summary>将文档列偏移转换为 VisualLine 可视列（含边界保护）</summary>
        private static bool TryGetPrefixVisualColumns(
            VisualLine visualLine,
            int markerStartOffset,
            int prefixEndOffset,
            out int markerStartColumn,
            out int prefixEndColumn)
        {
            markerStartColumn = 0;
            prefixEndColumn = 0;

            int startRel = Math.Min(markerStartOffset, visualLine.VisualLength);
            int endRel = Math.Min(prefixEndOffset, visualLine.VisualLength);
            if (endRel <= 0 || endRel < startRel)
            {
                return false;
            }

            try
            {
                markerStartColumn = visualLine.GetVisualColumn(startRel);
                prefixEndColumn = visualLine.GetVisualColumn(endRel);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        /// <summary>
        /// 在可用区内右对齐标记：优先落在标记槽内；过宽时可侵入前导缩进，但不越过正文左缘。
        /// </summary>
        private static double PlaceMarkerX(
            double lineStartX,
            double markerSlotStartX,
            double contentX,
            double markerWidth)
        {
            double rightLimit = contentX - ListMarkerContentGap;
            double x = rightLimit - markerWidth;

            // 优先不侵入标记槽左侧
            if (x >= markerSlotStartX)
            {
                return x;
            }

            // 槽不够宽：允许进入前导缩进，但仍不早于行首
            if (x >= lineStartX)
            {
                return x;
            }

            return lineStartX;
        }

        /// <summary>
        /// 按首行文字基线对齐标记，避免用行高居中导致上下被裁切
        /// </summary>
        private static double AlignMarkerToFirstLineBaseline(
            double firstLineTopViewport,
            double firstLineBaseline,
            double markerBaseline)
        {
            return firstLineTopViewport + firstLineBaseline - markerBaseline;
        }
    }
}

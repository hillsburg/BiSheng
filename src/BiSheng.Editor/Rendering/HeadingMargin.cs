using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BiSheng.Editor.Model;
using BiSheng.Editor.Parsing;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace BiSheng.Editor.Rendering;

/// <summary>
/// 左侧标题级别标记 Margin：继承 AvalonEdit 的 AbstractMargin，
/// 通过 OnRender + FormattedText 直接在 DrawingContext 上绘制 H1~H6 标签。
/// 零 UI 元素分配，与编辑器渲染管线自动同步。
///
/// <para>核心逻辑流程：</para>
/// <list type="number">
///   <item>订阅 TextView 的 VisualLinesChanged 和 ScrollOffsetChanged 事件，触发重绘</item>
///   <item>OnRender 中遍历所有可见 VisualLine，通过 LineMarkdownAnalyzer 判断是否为标题行</item>
///   <item>对标题行（H1~H6），使用 FormattedText 在行文字实际高度的垂直中心绘制标签</item>
/// </list>
///
/// <para>对齐要点：</para>
/// <list type="bullet">
///   <item>使用 VisualYPosition.LineTop + TextLine.Height/2 定位行中心，
///         而非 LineMiddle，以避免行间距（LineSpacing）导致的偏移</item>
///   <item>标签字号 = DefaultLineHeight × 0.6，确保标签不超出文字区域</item>
///   <item>水平方向在固定 32px 宽度内居中，垂直方向以文字实际高度居中</item>
/// </list>
/// </summary>
public class HeadingMargin : AbstractMargin
{
    /// <summary>Margin 固定宽度（像素）</summary>
    private const double MarginWidth = 32;

    /// <summary>标签字体族：等宽字体优先，保证 H1~H6 宽度一致</summary>
    private static readonly FontFamily LabelFont = new("Consolas, Segoe UI");

    /// <summary>标题标记颜色，跟随主题 HeadingColor 变化</summary>
    public Brush LabelBrush { get; set; } = new SolidColorBrush(Color.FromRgb(26, 26, 26));

    public HeadingMargin()
    {
        IsHitTestVisible = false;
        Cursor = Cursors.IBeam;
    }

    /// <summary>
    /// TextView 切换时订阅/取消订阅事件，确保重绘触发正常。
    /// 订阅的事件：
    /// - VisualLinesChanged：可视行重建时触发
    /// - ScrollOffsetChanged：滚动时触发
    /// </summary>
    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView != null)
        {
            oldTextView.VisualLinesChanged -= OnInvalidate;
            oldTextView.ScrollOffsetChanged -= OnInvalidate;
        }

        base.OnTextViewChanged(oldTextView, newTextView);

        if (newTextView != null)
        {
            newTextView.VisualLinesChanged += OnInvalidate;
            newTextView.ScrollOffsetChanged += OnInvalidate;
        }

        InvalidateVisual();
    }

    /// <summary>
    /// 文档切换时触发重绘
    /// </summary>
    protected override void OnDocumentChanged(TextDocument? oldDocument, TextDocument? newDocument)
    {
        base.OnDocumentChanged(oldDocument, newDocument);
        InvalidateVisual();
    }

    /// <summary>
    /// 事件回调：请求 WPF 重新绘制此 Margin
    /// </summary>
    private void OnInvalidate(object? sender, EventArgs e) => InvalidateVisual();

    /// <summary>
    /// 布局测量：返回固定宽度 32px，高度由父容器决定
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
        => new(MarginWidth, 0);

    /// <summary>
    /// 核心绘制逻辑：
    /// <list type="number">
    ///   <item>获取当前滚动偏移量 verticalOffset，用于将文档坐标转换为视口坐标</item>
    ///   <item>遍历所有可见 VisualLine，跳过已释放的行和重复文档行</item>
    ///   <item>通过 LineMarkdownAnalyzer.AnalyzeLineType 分析行类型，提取 H1~H6 级别</item>
    ///   <item>对标题行，计算文字实际高度的垂直中心（LineTop + TextLine.Height/2）</item>
    ///   <item>创建 FormattedText 标签，在 32px 宽度内水平居中、垂直居中对齐绘制</item>
    /// </list>
    /// </summary>
    protected override void OnRender(DrawingContext drawingContext)
    {
        var textView = TextView;
        if (textView == null || !textView.VisualLinesValid || textView.VisualLines.Count == 0)
            return;

        var document = Document;
        if (document == null) return;

        // 滚动偏移量：将文档绝对 Y 坐标转换为视口内相对 Y 坐标
        double verticalOffset = textView.VerticalOffset;
        // 标签字号 = 行高 × 0.6，确保标签视觉上略小于正文行高
        double emSize = textView.DefaultLineHeight * 0.6;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        int lastLineNum = -1;
        foreach (var visualLine in textView.VisualLines)
        {
            if (visualLine.IsDisposed) continue;

            // 同一文档行可能有多个 VisualLine（word wrap），只处理第一个
            int lineNum = visualLine.FirstDocumentLine.LineNumber;
            if (lineNum == lastLineNum) continue;
            lastLineNum = lineNum;

            var firstTextLine = visualLine.TextLines.FirstOrDefault();
            if (firstTextLine == null) continue;

            // 读取行文本并判断是否为标题
            var docLine = visualLine.FirstDocumentLine;
            if (docLine.Length == 0) continue;

            string lineText = document.GetText(docLine.Offset, docLine.Length);
            var lineType = LineMarkdownAnalyzer.AnalyzeLineType(lineText);

            // 提取标题级别（1~6），非标题返回 0
            int headingLevel = lineType switch
            {
                LineType.Heading1 => 1,
                LineType.Heading2 => 2,
                LineType.Heading3 => 3,
                LineType.Heading4 => 4,
                LineType.Heading5 => 5,
                LineType.Heading6 => 6,
                _ => 0
            };

            if (headingLevel == 0) continue;

            // 【关键】使用文字实际高度（TextLine.Height）的中心，
            // 而非 LineMiddle（包含 LineSpacing 间隙），确保标签与文字对齐
            double lineTop = visualLine.GetTextLineVisualYPosition(
                firstTextLine, VisualYPosition.LineTop) - verticalOffset;
            double lineMiddle = lineTop + firstTextLine.Height / 2;

            // 创建 FormattedText：零 UI 元素分配，直接绘制文本
            var ft = new FormattedText(
                $"H{headingLevel}",
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(LabelFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                emSize,
                LabelBrush,
                dpi);

            // 精确垂直居中：标签中心对齐行文字中心
            double y = lineMiddle - ft.Height / 2;
            // 水平居中：在 32px 固定宽度内居中
            double x = (MarginWidth - ft.Width) / 2;

            drawingContext.DrawText(ft, new Point(x, y));
        }
    }
}

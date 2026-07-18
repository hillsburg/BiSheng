using System.Windows;
using System.Windows.Media;

namespace BiSheng.Editor.Controls.MarkdownEditor.Themes
{
    /// <summary>
    /// Markdown 渲染样式主题
    /// </summary>
    public class MarkdownTheme
    {
        // 基础字体
        public FontFamily BaseFontFamily { get; set; } = new FontFamily("Segoe UI, Microsoft YaHei");
        public FontFamily CodeFontFamily { get; set; } = new FontFamily("Cascadia Code, Consolas");
        public double BaseFontSize { get; set; } = 14.0;

        // 基础颜色
        public Brush TextColor { get; set; } = new SolidColorBrush(Color.FromRgb(51, 51, 51));
        public Brush BackgroundColor { get; set; } = new SolidColorBrush(Color.FromRgb(255, 255, 255));

        // 标题样式
        public double Heading1FontSize { get; set; } = 28.0;
        public double Heading2FontSize { get; set; } = 24.0;
        public double Heading3FontSize { get; set; } = 20.0;
        public double Heading4FontSize { get; set; } = 18.0;
        public double Heading5FontSize { get; set; } = 16.0;
        public double Heading6FontSize { get; set; } = 14.0;
        public Brush HeadingColor { get; set; } = new SolidColorBrush(Color.FromRgb(26, 26, 26));

        // 代码块
        public Brush InlineCodeBackground { get; set; } = new SolidColorBrush(Color.FromRgb(243, 243, 243));
        public Brush InlineCodeForeground { get; set; } = new SolidColorBrush(Color.FromRgb(199, 37, 78));
        public Brush CodeBlockBackground { get; set; } = new SolidColorBrush(Color.FromRgb(248, 248, 248));
        public Brush CodeBlockBorder { get; set; } = new SolidColorBrush(Color.FromRgb(220, 220, 220));

        // 引用块
        public Brush QuoteBorderColor { get; set; } = new SolidColorBrush(Color.FromRgb(200, 200, 200));
        public Brush QuoteTextColor { get; set; } = new SolidColorBrush(Color.FromRgb(119, 119, 119));
        public Brush QuoteBackground { get; set; } = new SolidColorBrush(Color.FromRgb(249, 249, 249));

        // 链接
        public Brush LinkColor { get; set; } = new SolidColorBrush(Color.FromRgb(0, 122, 204));

        // 分割线
        public Brush HorizontalRuleColor { get; set; } = new SolidColorBrush(Color.FromRgb(200, 200, 200));

        // 光标
        public Brush CaretColor { get; set; } = new SolidColorBrush(Color.FromRgb(51, 51, 51));

        // 列表
        public Brush BulletColor { get; set; } = new SolidColorBrush(Color.FromRgb(100, 100, 100));

        /// <summary>
        /// 默认浅色主题
        /// </summary>
        public static MarkdownTheme Light => new MarkdownTheme();

        /// <summary>
        /// 深色主题
        /// </summary>
        public static MarkdownTheme Dark => new MarkdownTheme
        {
            TextColor = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            BackgroundColor = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            HeadingColor = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
            InlineCodeBackground = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
            InlineCodeForeground = new SolidColorBrush(Color.FromRgb(230, 100, 130)),
            CodeBlockBackground = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            CodeBlockBorder = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            QuoteBorderColor = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            QuoteTextColor = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
            QuoteBackground = new SolidColorBrush(Color.FromRgb(35, 35, 35)),
            HorizontalRuleColor = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            BulletColor = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
            LinkColor = new SolidColorBrush(Color.FromRgb(86, 156, 214)),
            CaretColor = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
        };

        /// <summary>
        /// Latte 暖色调主题：咖啡/琥珀色暖色配色
        /// </summary>
        public static MarkdownTheme Latte => new MarkdownTheme
        {
            TextColor = new SolidColorBrush(Color.FromRgb(60, 50, 40)),
            BackgroundColor = new SolidColorBrush(Color.FromRgb(255, 252, 248)),
            HeadingColor = new SolidColorBrush(Color.FromRgb(44, 37, 32)),
            InlineCodeBackground = new SolidColorBrush(Color.FromRgb(242, 236, 227)),
            InlineCodeForeground = new SolidColorBrush(Color.FromRgb(166, 124, 82)),
            CodeBlockBackground = new SolidColorBrush(Color.FromRgb(248, 244, 239)),
            CodeBlockBorder = new SolidColorBrush(Color.FromRgb(221, 212, 200)),
            QuoteBorderColor = new SolidColorBrush(Color.FromRgb(196, 166, 125)),
            QuoteTextColor = new SolidColorBrush(Color.FromRgb(138, 125, 112)),
            QuoteBackground = new SolidColorBrush(Color.FromRgb(250, 246, 241)),
            LinkColor = new SolidColorBrush(Color.FromRgb(166, 124, 82)),
            HorizontalRuleColor = new SolidColorBrush(Color.FromRgb(221, 212, 200)),
            BulletColor = new SolidColorBrush(Color.FromRgb(166, 124, 82)),
            CaretColor = new SolidColorBrush(Color.FromRgb(60, 50, 40)),
        };
    }
}

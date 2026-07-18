using System.Windows;
using System.Windows.Media;

namespace BiSheng.Latte.Models;

/// <summary>字体选择列表项（系统已安装）</summary>
public sealed class FontPickItem
{
    /// <summary>下拉展示名</summary>
    public string Display { get; init; } = "";

    /// <summary>写入主题的字体栈（可逗号回退）</summary>
    public string Source { get; init; } = "";

    /// <summary>是否为列表置顶推荐项</summary>
    public bool IsRecommended { get; init; }

    /// <summary>当前环境是否可用</summary>
    public bool IsAvailable { get; init; }
}

/// <summary>
/// 内容字体目录与解析：仅使用系统已装字体，不内置正文字体文件
/// </summary>
public static class FontCatalog
{
    /// <summary>默认编辑器字体栈</summary>
    public const string DefaultStack = "Microsoft YaHei, Segoe UI";

    /// <summary>最终保底</summary>
    private static readonly string[] UltimateFallbacks = ["Microsoft YaHei", "Segoe UI"];

    /// <summary>解析主题内容字体栈（外观覆盖优先，已展开回退）</summary>
    public static string ResolveEffectiveStack(ThemeDefinition theme, AppearanceSettings settings)
    {
        var preferred = settings.TryGetThemeContentFont(theme.Name) ?? theme.ContentFontFamily;
        return ExpandStack(preferred);
    }

    /// <summary>解析某主题当前应显示的字体栈（未展开，供 UI 同步下拉）</summary>
    public static string ResolvePreferredStack(ThemeDefinition theme, AppearanceSettings settings)
    {
        return settings.TryGetThemeContentFont(theme.Name) ?? theme.ContentFontFamily;
    }

    /// <summary>由字体栈创建 FontFamily</summary>
    public static FontFamily CreateFontFamily(string stack)
    {
        return new FontFamily(string.IsNullOrWhiteSpace(stack) ? ExpandStack(DefaultStack) : stack);
    }

    /// <summary>将 ContentFont 写入应用资源（供 DynamicResource 刷新）</summary>
    public static void ApplyContentFontToResources(ResourceDictionary res, FontFamily fontFamily)
    {
        res["ContentFont"] = fontFamily;
    }

    /// <summary>展开回退链：去重并追加最终保底</summary>
    public static string ExpandStack(string? preferred)
    {
        var parts = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string token)
        {
            token = token.Trim();
            if (string.IsNullOrEmpty(token) || !seen.Add(token))
            {
                return;
            }

            parts.Add(token);
        }

        var raw = string.IsNullOrWhiteSpace(preferred) ? DefaultStack : preferred;
        foreach (var segment in raw.Split(','))
        {
            Add(segment.Trim());
        }

        foreach (var fb in UltimateFallbacks)
        {
            Add(fb);
        }

        return string.Join(", ", parts);
    }

    /// <summary>取栈中第一个族名，用于 UI 提示</summary>
    public static string DescribeStack(string? stack)
    {
        if (string.IsNullOrWhiteSpace(stack))
        {
            return "微软雅黑";
        }

        var first = stack.Split(',')[0].Trim();
        if (string.IsNullOrEmpty(first))
        {
            return "微软雅黑";
        }

        if (first.Equals("Microsoft YaHei", StringComparison.OrdinalIgnoreCase))
        {
            return "微软雅黑";
        }

        if (first.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase))
        {
            return "Segoe UI";
        }

        return IsFamilyAvailable(first) ? first : $"{first}（将回退到雅黑等）";
    }

    /// <summary>首选族是否当前可用（不含最终回退）</summary>
    public static bool IsPreferredAvailable(string? preferred)
    {
        if (string.IsNullOrWhiteSpace(preferred))
        {
            return false;
        }

        var first = preferred.Split(',')[0].Trim();
        return IsFamilyAvailable(first);
    }

    /// <summary>族名是否系统已安装</summary>
    public static bool IsFamilyAvailable(string familyName)
    {
        if (string.IsNullOrWhiteSpace(familyName))
        {
            return false;
        }

        return Fonts.SystemFontFamilies.Any(f =>
            f.Source.Equals(familyName, StringComparison.OrdinalIgnoreCase)
            || f.Source.Contains(familyName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>字体下拉：默认微软雅黑置顶，其余为系统字体（按名称排序）</summary>
    public static List<FontPickItem> BuildPickerItems()
    {
        var items = new List<FontPickItem>
        {
            new()
            {
                Display = "微软雅黑（默认）",
                Source = DefaultStack,
                IsRecommended = true,
                IsAvailable = IsFamilyAvailable("Microsoft YaHei") || IsFamilyAvailable("Segoe UI")
            }
        };

        var pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft YaHei",
            "Segoe UI"
        };

        foreach (var font in Fonts.SystemFontFamilies.OrderBy(f => f.Source, StringComparer.OrdinalIgnoreCase))
        {
            if (pinned.Contains(font.Source))
            {
                continue;
            }

            items.Add(new FontPickItem
            {
                Display = font.Source,
                Source = font.Source,
                IsRecommended = false,
                IsAvailable = true
            });
        }

        return items;
    }

    /// <summary>在列表中选中与 stored 最匹配的项</summary>
    public static FontPickItem? FindMatchingItem(IEnumerable<FontPickItem> items, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        var list = items as IList<FontPickItem> ?? items.ToList();
        var exact = list.FirstOrDefault(i =>
            i.Source.Equals(stored, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            return exact;
        }

        var first = stored.Split(',')[0].Trim();
        return list.FirstOrDefault(i =>
                   i.Source.StartsWith(first, StringComparison.OrdinalIgnoreCase)
                   || i.Display.Contains(first, StringComparison.OrdinalIgnoreCase))
               ?? list.FirstOrDefault(i =>
                   i.Source.Contains(first, StringComparison.OrdinalIgnoreCase));
    }
}

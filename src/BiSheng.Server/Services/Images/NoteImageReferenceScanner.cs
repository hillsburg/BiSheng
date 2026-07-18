using System.Text.RegularExpressions;

namespace BiSheng.Server.Services.Images;

/// <summary>
/// 从 Markdown 笔记正文中提取图片 UUID（Markdown 为唯一引用真相源）
/// </summary>
public static partial class NoteImageReferenceScanner
{
    /// <summary>
    /// 匹配：
    /// - bisheng://img/{guid}
    /// - /api/images/{guid}
    /// </summary>
    [GeneratedRegex(
        @"(?:bisheng://img/|/api/images/)([0-9a-fA-F\-]{36})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImageIdRegex();

    /// <summary>从单篇正文提取图片 Id 集合</summary>
    /// <param name="content">笔记 Markdown 正文</param>
    public static HashSet<Guid> ExtractImageIds(string? content)
    {
        var ids = new HashSet<Guid>();
        if (string.IsNullOrEmpty(content))
        {
            return ids;
        }

        foreach (Match match in ImageIdRegex().Matches(content))
        {
            if (Guid.TryParse(match.Groups[1].Value, out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>合并多篇正文中的图片引用</summary>
    /// <param name="contents">多篇笔记正文</param>
    public static HashSet<Guid> ExtractImageIds(IEnumerable<string?> contents)
    {
        var ids = new HashSet<Guid>();
        foreach (var content in contents)
        {
            foreach (var id in ExtractImageIds(content))
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}

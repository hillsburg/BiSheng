using System.Text;

namespace BiSheng.Latte.Services.Search;

/// <summary>将 Markdown 转为可搜索 PlainText，并维护 Plain→Markdown 偏移映射</summary>
public static class NoteSearchTextExtractor
{
    private const int ContextRadius = 40;

    /// <summary>提取 PlainText 与偏移映射</summary>
    public static NoteSearchExtraction Extract(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return new NoteSearchExtraction();
        }

        var plain = new StringBuilder(markdown.Length);
        var map = new List<int>(markdown.Length);
        var i = 0;

        while (i < markdown.Length)
        {
            if (TrySkipCodeFence(markdown, ref i))
            {
                continue;
            }

            if (IsLineStart(markdown, i) && TrySkipHeadingMarker(markdown, ref i))
            {
                continue;
            }

            if (IsLineStart(markdown, i) && TrySkipListMarker(markdown, ref i))
            {
                continue;
            }

            if (markdown[i] == '!' && i + 1 < markdown.Length && markdown[i + 1] == '[')
            {
                if (TryConsumeBracketLink(markdown, ref i, plain, map))
                {
                    continue;
                }
            }

            if (markdown[i] == '[')
            {
                if (TryConsumeBracketLink(markdown, ref i, plain, map))
                {
                    continue;
                }
            }

            if (markdown[i] == '`')
            {
                i++;
                while (i < markdown.Length && markdown[i] != '`')
                {
                    Emit(markdown, i, plain, map);
                    i++;
                }

                if (i < markdown.Length)
                {
                    i++;
                }

                continue;
            }

            if (markdown[i] is '*' or '_' or '~')
            {
                i++;
                continue;
            }

            Emit(markdown, i, plain, map);
            i++;
        }

        return new NoteSearchExtraction
        {
            PlainText = plain.ToString(),
            PlainToMarkdown = map.ToArray()
        };
    }

    /// <summary>在 PlainText 中查找所有命中（忽略大小写）</summary>
    public static IReadOnlyList<(int Offset, int Length)> FindAll(string plainText, string query)
    {
        var hits = new List<(int, int)>();
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrEmpty(plainText))
        {
            return hits;
        }

        var q = query.Trim();
        var start = 0;
        while (start <= plainText.Length - q.Length)
        {
            var idx = plainText.IndexOf(q, start, StringComparison.CurrentCultureIgnoreCase);
            if (idx < 0)
            {
                break;
            }

            hits.Add((idx, q.Length));
            start = idx + Math.Max(1, q.Length);
        }

        return hits;
    }

    /// <summary>生成命中上下文摘要</summary>
    public static string BuildSnippet(string plainText, int offset, int length)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        var start = Math.Max(0, offset - ContextRadius);
        var end = Math.Min(plainText.Length, offset + length + ContextRadius);
        var snippet = plainText[start..end].Replace('\n', ' ').Replace('\r', ' ');
        if (start > 0)
        {
            snippet = "…" + snippet;
        }

        if (end < plainText.Length)
        {
            snippet += "…";
        }

        return snippet;
    }

    private static void Emit(string markdown, int index, StringBuilder plain, List<int> map)
    {
        var c = markdown[index];
        if (c is '\r')
        {
            return;
        }

        if (c is '\n' or '\t')
        {
            c = ' ';
        }

        if (c == ' ' && plain.Length > 0 && plain[^1] == ' ')
        {
            return;
        }

        plain.Append(c);
        map.Add(index);
    }

    private static bool IsLineStart(string text, int index)
    {
        return index == 0 || text[index - 1] is '\n' or '\r';
    }

    private static bool TrySkipCodeFence(string text, ref int index)
    {
        if (index + 2 >= text.Length
            || text[index] != '`'
            || text[index + 1] != '`'
            || text[index + 2] != '`')
        {
            return false;
        }

        index += 3;
        while (index < text.Length)
        {
            if (index + 2 < text.Length
                && text[index] == '`'
                && text[index + 1] == '`'
                && text[index + 2] == '`')
            {
                index += 3;
                return true;
            }

            index++;
        }

        return true;
    }

    private static bool TrySkipHeadingMarker(string text, ref int index)
    {
        var start = index;
        while (index < text.Length && text[index] == '#')
        {
            index++;
        }

        if (index == start || index >= text.Length || text[index] != ' ')
        {
            index = start;
            return false;
        }

        index++;
        return true;
    }

    private static bool TrySkipListMarker(string text, ref int index)
    {
        var start = index;
        if (text[index] is '-' or '*' or '+')
        {
            index++;
            if (index < text.Length && text[index] == ' ')
            {
                index++;
                return true;
            }
        }

        index = start;
        while (index < text.Length && char.IsDigit(text[index]))
        {
            index++;
        }

        if (index > start && index < text.Length && text[index] == '.')
        {
            index++;
            if (index < text.Length && text[index] == ' ')
            {
                index++;
                return true;
            }
        }

        index = start;
        return false;
    }

    private static bool TryConsumeBracketLink(
        string text,
        ref int index,
        StringBuilder plain,
        List<int> map)
    {
        var start = index;
        var hasBang = text[index] == '!';
        var bracketIndex = hasBang ? index + 1 : index;
        if (bracketIndex >= text.Length || text[bracketIndex] != '[')
        {
            return false;
        }

        var innerStart = bracketIndex + 1;
        var innerEnd = innerStart;
        while (innerEnd < text.Length && text[innerEnd] != ']')
        {
            innerEnd++;
        }

        if (innerEnd >= text.Length)
        {
            return false;
        }

        var parenStart = innerEnd + 1;
        if (parenStart >= text.Length || text[parenStart] != '(')
        {
            return false;
        }

        var depth = 1;
        var parenEnd = parenStart + 1;
        while (parenEnd < text.Length && depth > 0)
        {
            if (text[parenEnd] == '(')
            {
                depth++;
            }
            else if (text[parenEnd] == ')')
            {
                depth--;
            }

            parenEnd++;
        }

        if (depth != 0)
        {
            return false;
        }

        for (var i = innerStart; i < innerEnd; i++)
        {
            Emit(text, i, plain, map);
        }

        index = parenEnd;
        return true;
    }
}

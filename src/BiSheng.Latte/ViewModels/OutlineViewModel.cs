using System.Collections.ObjectModel;
using BiSheng.Latte.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiSheng.Latte.ViewModels;

/// <summary>笔记大纲 ViewModel：从 Markdown 文本解析标题树</summary>
public partial class OutlineViewModel : ObservableObject
{
    /// <summary>大纲树根节点</summary>
    public ObservableCollection<OutlineItem> Items { get; } = new();

    /// <summary>从 Markdown 全文刷新大纲（同步，仅适合短文本测试）</summary>
    public void RefreshFromText(string? markdown) =>
        ApplyHeadings(ParseFlatHeadings(markdown));

    /// <summary>在线程池解析标题，避免阻塞 UI</summary>
    public static List<OutlineItem> ParseFlatHeadings(string? markdown)
    {
        var flatItems = new List<OutlineItem>();
        if (string.IsNullOrEmpty(markdown))
        {
            return flatItems;
        }

        var lineNumber = 0;
        ReadOnlySpan<char> remaining = markdown.AsSpan();
        while (remaining.Length > 0)
        {
            var newline = remaining.IndexOf('\n');
            var line = newline >= 0 ? remaining[..newline] : remaining;
            if (line.Length > 0 && line[^1] == '\r')
            {
                line = line[..^1];
            }

            TryParseHeadingLine(line, lineNumber, flatItems);

            lineNumber++;
            remaining = newline >= 0 ? remaining[(newline + 1)..] : ReadOnlySpan<char>.Empty;
        }

        return flatItems;
    }

    /// <summary>将解析结果应用到树（须在 UI 线程调用）</summary>
    public void ApplyHeadings(IReadOnlyList<OutlineItem> flatItems)
    {
        Items.Clear();
        if (flatItems.Count == 0)
        {
            return;
        }

        var roots = BuildOutlineTree(flatItems);
        foreach (var root in roots)
        {
            Items.Add(root);
        }
    }

    private static bool TryParseHeadingLine(ReadOnlySpan<char> line, int lineNumber, List<OutlineItem> output)
    {
        if (line.IsEmpty || line[0] != '#')
        {
            return false;
        }

        var level = 0;
        while (level < line.Length && level < 6 && line[level] == '#')
        {
            level++;
        }

        if (level == 0 || level >= line.Length || line[level] != ' ')
        {
            return false;
        }

        var titleSpan = line[(level + 1)..].Trim();
        if (titleSpan.IsEmpty)
        {
            return false;
        }

        output.Add(new OutlineItem
        {
            Level = level,
            Title = titleSpan.ToString(),
            LineNumber = lineNumber
        });
        return true;
    }

    /// <summary>将扁平标题列表构建为树形结构</summary>
    private static ObservableCollection<OutlineItem> BuildOutlineTree(IReadOnlyList<OutlineItem> items)
    {
        var root = new ObservableCollection<OutlineItem>();
        var stack = new Stack<OutlineItem>();

        foreach (var item in items)
        {
            while (stack.Count > 0 && stack.Peek().Level >= item.Level)
            {
                stack.Pop();
            }

            if (stack.Count == 0)
            {
                root.Add(item);
            }
            else
            {
                stack.Peek().Children.Add(item);
            }

            stack.Push(item);
        }

        return root;
    }
}

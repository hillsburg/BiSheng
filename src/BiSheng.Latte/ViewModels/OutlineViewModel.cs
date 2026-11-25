using System.Collections.ObjectModel;
using BiSheng.Latte.Models;
using ICSharpCode.AvalonEdit.Document;

namespace BiSheng.Latte.ViewModels;

/// <summary>解析标题快照，并根据文档锚点增量更新当前笔记的大纲。</summary>
public class OutlineViewModel
{
    private TextDocument? _document;
    private Dictionary<OutlineItem, OutlineItem?> _parents = new();

    /// <summary>大纲树根节点</summary>
    public ObservableCollection<OutlineItem> Items { get; } = new();

    /// <summary>在线程池解析标题，避免阻塞 UI</summary>
    internal static List<OutlineHeading> ParseFlatHeadings(string? markdown)
    {
        var flatItems = new List<OutlineHeading>();
        if (string.IsNullOrEmpty(markdown))
        {
            return flatItems;
        }

        var lineNumber = 1;
        ReadOnlySpan<char> remaining = markdown.AsSpan();
        while (remaining.Length > 0)
        {
            int newline = remaining.IndexOfAny('\r', '\n');
            var line = newline >= 0 ? remaining[..newline] : remaining;
            var title = GetHeadingTitle(line, out int level);
            if (!title.IsEmpty)
            {
                flatItems.Add(new OutlineHeading(lineNumber, level, title.ToString()));
            }

            if (newline < 0)
            {
                break;
            }

            // 换行计数遵循 TextDocument，保证混合 CRLF、LF 和 CR 的笔记也能准确定位。
            int next = newline + 1;
            if (remaining[newline] == '\r' && next < remaining.Length && remaining[next] == '\n')
            {
                next++;
            }

            remaining = remaining[next..];
            lineNumber++;
        }

        return flatItems;
    }

    /// <summary>切换或关闭笔记时丢弃旧节点及锚点，同一个编辑器文档可能被下篇笔记复用。</summary>
    public void Reset()
    {
        _document = null;
        _parents.Clear();
        if (Items.Count > 0)
        {
            Items.Clear();
        }
    }

    /// <summary>在 UI 线程将当前文档的标题快照同步到大纲，复用锚点仍在对应标题行的节点。</summary>
    internal void ApplyHeadings(TextDocument document, IReadOnlyList<OutlineHeading> flatItems)
    {
        if (!ReferenceEquals(_document, document))
        {
            Reset();
            _document = document;
        }

        var existing = GetItemsByLine(document);
        var roots = new List<OutlineItem>();
        var parents = new Dictionary<OutlineItem, OutlineItem?>();
        var children = new Dictionary<OutlineItem, List<OutlineItem>>();
        var stack = new Stack<OutlineItem>();
        foreach (var heading in flatItems)
        {
            if (!existing.TryGetValue(heading.LineNumber, out var item))
            {
                var line = document.GetLineByNumber(heading.LineNumber);
                // 锚点放在首个 # 之后，保证整行删除时它随原位置消失。
                item = new OutlineItem(document.CreateAnchor(line.Offset + 1), heading);
            }
            else
            {
                item.Title = heading.Title;
                item.Level = heading.Level;
            }

            while (stack.TryPeek(out var previous) && previous.Level >= item.Level)
            {
                stack.Pop();
            }

            if (stack.TryPeek(out var parent))
            {
                if (!children.TryGetValue(parent, out var siblings))
                {
                    siblings = new List<OutlineItem>();
                    children.Add(parent, siblings);
                }

                siblings.Add(item);
            }
            else
            {
                roots.Add(item);
            }

            parents.Add(item, parent);
            stack.Push(item);
        }

        ApplyTree(roots, parents, children);
    }

    /// <summary>根据实时锚点定位标题，等待后台刷新期间也能跳到文档中的正确位置。</summary>
    internal DocumentLine? FindHeadingLine(TextDocument document, OutlineItem item)
    {
        if (!ReferenceEquals(document, _document) || !_parents.ContainsKey(item) || item.Anchor.IsDeleted)
        {
            return null;
        }

        var line = document.GetLineByOffset(item.Anchor.Offset);
        return GetHeadingTitle(document.GetText(line.Offset, line.Length), out _).IsEmpty ? null : line;
    }

    /// <summary>建立实时行号到旧节点的映射。合并两行时，保留锚点最靠近行首的旧节点。</summary>
    private Dictionary<int, OutlineItem> GetItemsByLine(TextDocument document)
    {
        var existing = new Dictionary<int, OutlineItem>(_parents.Count);
        foreach (var item in _parents.Keys)
        {
            if (item.Anchor.IsDeleted)
            {
                continue;
            }

            int offset = item.Anchor.Offset;
            int line = document.GetLineByOffset(offset).LineNumber;
            if (!existing.TryGetValue(line, out var earlier) || offset < earlier.Anchor.Offset)
            {
                existing[line] = item;
            }
        }

        return existing;
    }

    /// <summary>只变更增删或换父级的节点，避免正文编辑导致 TreeView 重建容器。</summary>
    private void ApplyTree(
        List<OutlineItem> roots,
        Dictionary<OutlineItem, OutlineItem?> parents,
        Dictionary<OutlineItem, List<OutlineItem>> children)
    {
        var selected = _parents.Keys.FirstOrDefault(item => item.IsSelected);
        foreach (var (item, oldParent) in _parents)
        {
            if (!parents.TryGetValue(item, out var newParent) || !ReferenceEquals(oldParent, newParent))
            {
                (oldParent?.Children ?? Items).Remove(item);
            }
        }

        ApplyOrder(Items, roots);
        foreach (var (parent, siblings) in children)
        {
            ApplyOrder(parent.Children, siblings);
        }

        // WPF 移除选中容器时可能自动选中父级或首项，恢复用户原先选的节点。
        foreach (var item in parents.Keys)
        {
            if (!ReferenceEquals(item, selected) && item.IsSelected)
            {
                item.IsSelected = false;
            }
        }

        if (selected != null)
        {
            if (parents.ContainsKey(selected))
            {
                RevealMovedSelection(selected, parents);
                selected.IsSelected = true;
            }
            else
            {
                selected.IsSelected = false;
            }
        }

        _parents = parents;
    }

    /// <summary>选中节点或其祖先换了父级时，展开它的新路径以保持选中项可见。</summary>
    private void RevealMovedSelection(OutlineItem selected, Dictionary<OutlineItem, OutlineItem?> parents)
    {
        for (OutlineItem? item = selected; item != null; item = parents[item])
        {
            if (!_parents.TryGetValue(item, out var oldParent) || !ReferenceEquals(oldParent, parents[item]))
            {
                for (var parent = parents[selected]; parent != null; parent = parents[parent])
                {
                    parent.IsExpanded = true;
                }

                return;
            }
        }
    }

    /// <summary>在原集合中插入或移动节点，已经在正确位置的节点不触发集合变更。</summary>
    private static void ApplyOrder(ObservableCollection<OutlineItem> current, List<OutlineItem> desired)
    {
        for (int index = 0; index < desired.Count; index++)
        {
            var item = desired[index];
            if (index == current.Count)
            {
                current.Add(item);
            }
            else if (!ReferenceEquals(current[index], item))
            {
                int previousIndex = current.IndexOf(item);
                if (previousIndex >= 0)
                {
                    current.Move(previousIndex, index);
                }
                else
                {
                    current.Insert(index, item);
                }
            }
        }
    }

    /// <summary>返回标题正文，同时识别级别；导航和后台解析使用同一规则。</summary>
    private static ReadOnlySpan<char> GetHeadingTitle(ReadOnlySpan<char> line, out int level)
    {
        level = 0;
        while (level < line.Length && level < 6 && line[level] == '#')
        {
            level++;
        }

        return level > 0 && level < line.Length && line[level] == ' '
            ? line[(level + 1)..].Trim()
            : default;
    }
}

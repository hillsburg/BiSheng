using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using ICSharpCode.AvalonEdit.Document;

namespace BiSheng.Latte.Models;

/// <summary>
/// 笔记大纲条目，表示文档中的一个标题节点
/// </summary>
public partial class OutlineItem : ObservableObject
{
    /// <summary>标题文本（去除 # 前缀后的内容）</summary>
    [ObservableProperty]
    private string _title;

    /// <summary>标题级别（1-6，对应 H1-H6）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FontWeight))]
    private int _level;

    /// <summary>选中状态由数据节点保存，节点换父级时可以恢复。</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>展开状态由数据节点保存，供重建的树控件容器使用。</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>跟踪标题在当前文档中的位置，不依赖标题文字或快照行号。</summary>
    internal TextAnchor Anchor { get; }

    /// <summary>字重（H1/H2 加粗，其余正常）</summary>
    public FontWeight FontWeight => Level <= 2 ? FontWeights.Bold : FontWeights.Normal;

    /// <summary>子标题列表</summary>
    public ObservableCollection<OutlineItem> Children { get; } = new();

    /// <summary>为当前笔记中的标题创建可绑定的大纲节点。</summary>
    internal OutlineItem(TextAnchor anchor, OutlineHeading heading)
    {
        Anchor = anchor;
        _title = heading.Title;
        _level = heading.Level;
    }
}

/// <summary>后台解析得到的标题快照，行号从 1 开始，与 AvalonEdit 一致。</summary>
internal readonly record struct OutlineHeading(int LineNumber, int Level, string Title);

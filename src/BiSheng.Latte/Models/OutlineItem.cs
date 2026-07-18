using System.Collections.ObjectModel;
using System.Windows;

namespace BiSheng.Latte.Models;

/// <summary>
/// 笔记大纲条目，表示文档中的一个标题节点
/// </summary>
public class OutlineItem
{
    /// <summary>标题文本（去除 # 前缀后的内容）</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>标题级别（1-6，对应 H1-H6）</summary>
    public int Level { get; set; }

    /// <summary>标题所在的文档行号（0-based）</summary>
    public int LineNumber { get; set; }

    /// <summary>字重（H1/H2 加粗，其余正常）</summary>
    public FontWeight FontWeight => Level <= 2 ? FontWeights.Bold : FontWeights.Normal;

    /// <summary>子标题列表</summary>
    public ObservableCollection<OutlineItem> Children { get; } = new();
}

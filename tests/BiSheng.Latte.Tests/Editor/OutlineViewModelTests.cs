using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using BiSheng.Latte.ViewModels;
using ICSharpCode.AvalonEdit.Document;

namespace BiSheng.Latte.Tests.Editor;

/// <summary>验证同一笔记在增量编辑时保留标题节点及其交互状态。</summary>
public sealed class OutlineViewModelTests
{
    /// <summary>正文修改和前方增行不重建集合，跳转在去抖期间也使用实时位置。</summary>
    [Fact]
    public void BodyEdits_PreserveNodesAndKeepNavigationCurrent()
    {
        var document = new TextDocument("# 目录\r\n正文\n## 细节\r最后一段");
        var outline = new OutlineViewModel();
        Refresh(outline, document);
        var root = Assert.Single(outline.Items);
        var selected = Assert.Single(root.Children);
        root.IsExpanded = true;
        selected.IsSelected = true;
        var changes = new List<NotifyCollectionChangedAction>();
        outline.Items.CollectionChanged += (_, e) => changes.Add(e.Action);
        root.Children.CollectionChanged += (_, e) => changes.Add(e.Action);

        document.Insert(document.GetLineByNumber(2).Offset, "补充正文\n");
        Assert.Equal(4, outline.FindHeadingLine(document, selected)?.LineNumber);
        Refresh(outline, document);

        Assert.Same(root, Assert.Single(outline.Items));
        Assert.Same(selected, Assert.Single(root.Children));
        Assert.True(root.IsExpanded);
        Assert.True(selected.IsSelected);
        Assert.Empty(changes);
    }

    /// <summary>插入同名标题时选中原来的标题，原地改名时通知绑定更新。</summary>
    [Fact]
    public void DuplicateInsertedBeforeSelection_PreservesOriginalHeading()
    {
        var document = new TextDocument("# 目录\n## 同名\n## 同名\n尾部");
        var outline = new OutlineViewModel();
        Refresh(outline, document);
        var root = Assert.Single(outline.Items);
        var selected = root.Children[1];
        selected.IsSelected = true;
        var changes = new List<NotifyCollectionChangedAction>();
        root.Children.CollectionChanged += (_, e) => changes.Add(e.Action);
        var properties = new List<string?>();
        selected.PropertyChanged += (_, e) => properties.Add(e.PropertyName);

        document.Insert(document.GetLineByNumber(3).Offset, "## 同名\n");
        document.Replace(document.GetLineByNumber(4).Offset + 3, 2, "改名");
        Refresh(outline, document);

        Assert.Same(selected, root.Children[2]);
        Assert.True(selected.IsSelected);
        Assert.Equal("改名", selected.Title);
        Assert.Contains(nameof(selected.Title), properties);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Add }, changes);
    }

    /// <summary>标题换父级后保留选中并展开新路径，删除或切换笔记时丢弃旧选中。</summary>
    [Fact]
    public void ReparentDeleteAndReset_UpdateOnlyTheAffectedHeading()
    {
        var document = new TextDocument("# 甲\n## 小节\n# 乙\n# 丙");
        var outline = new OutlineViewModel();
        Refresh(outline, document);
        var first = outline.Items[0];
        var second = outline.Items[1];
        var third = outline.Items[2];
        var selected = Assert.Single(first.Children);
        selected.IsSelected = true;

        document.Insert(document.GetLineByNumber(2).Offset, "# 新章节\n");
        Refresh(outline, document);
        var newParent = outline.Items[1];
        Assert.Same(selected, Assert.Single(newParent.Children));
        Assert.Empty(first.Children);
        Assert.True(newParent.IsExpanded);
        Assert.True(selected.IsSelected);
        Assert.Same(second, outline.Items[2]);
        Assert.Same(third, outline.Items[3]);

        var line = document.GetLineByNumber(3);
        document.Remove(line.Offset, line.TotalLength);
        Assert.Null(outline.FindHeadingLine(document, selected));
        Refresh(outline, document);
        Assert.Empty(newParent.Children);
        Assert.False(selected.IsSelected);

        // 切换到同标题的笔记时，宿主仍复用同一个 TextDocument。
        outline.Reset();
        document.Text = "# 甲";
        Refresh(outline, document);
        var next = Assert.Single(outline.Items);
        Assert.NotSame(first, next);
        Assert.False(next.IsSelected);
        Assert.Null(outline.FindHeadingLine(document, first));
    }

    /// <summary>用真实 TreeView 验证换父级后保留选中，删除后不会误选其他标题。</summary>
    [StaFact]
    public void TreeSelection_FollowsReparentedHeadingAndClearsOnDeletion()
    {
        var document = new TextDocument("# 第一章\n## 小节\n# 第二章");
        var outline = new OutlineViewModel();
        Refresh(outline, document);
        // 同主窗口一样绑定节点状态，布局后可观察真实的 WPF SelectedItem。
        var tree = (TreeView)XamlReader.Parse("""
            <TreeView xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <TreeView.Resources>
                    <Style TargetType="TreeViewItem">
                        <Setter Property="IsExpanded" Value="{Binding IsExpanded, Mode=TwoWay}" />
                        <Setter Property="IsSelected" Value="{Binding IsSelected, Mode=TwoWay}" />
                    </Style>
                </TreeView.Resources>
                <TreeView.ItemTemplate>
                    <HierarchicalDataTemplate ItemsSource="{Binding Children}">
                        <TextBlock Text="{Binding Title}" />
                    </HierarchicalDataTemplate>
                </TreeView.ItemTemplate>
            </TreeView>
            """);
        tree.ItemsSource = outline.Items;
        var first = outline.Items[0];
        first.IsExpanded = true;
        LayoutTree(tree);
        var selected = Assert.Single(first.Children);
        var parent = Assert.IsType<TreeViewItem>(tree.ItemContainerGenerator.ContainerFromItem(first));
        var child = Assert.IsType<TreeViewItem>(parent.ItemContainerGenerator.ContainerFromItem(selected));
        child.SetCurrentValue(TreeViewItem.IsSelectedProperty, true);
        Assert.True(selected.IsSelected);
        Assert.Same(selected, tree.SelectedItem);

        document.Insert(document.GetLineByNumber(2).Offset, "# 新章\n");
        Refresh(outline, document);
        LayoutTree(tree);
        Assert.Same(selected, tree.SelectedItem);
        Assert.True(outline.Items[1].IsExpanded);

        var line = document.GetLineByNumber(3);
        document.Remove(line.Offset, line.TotalLength);
        Refresh(outline, document);
        LayoutTree(tree);
        Assert.Null(tree.SelectedItem);
    }

    /// <summary>生成足够大的视口中的树容器，无需打开窗口。</summary>
    private static void LayoutTree(TreeView tree)
    {
        var size = new Size(360, 360);
        tree.Measure(size);
        tree.Arrange(new Rect(size));
        tree.UpdateLayout();
    }

    /// <summary>同步调用与编辑器宿主相同的解析及应用入口。</summary>
    private static void Refresh(OutlineViewModel outline, TextDocument document)
    {
        outline.ApplyHeadings(document, OutlineViewModel.ParseFlatHeadings(document.Text));
    }
}

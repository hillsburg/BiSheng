using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BiSheng.Latte.Controls.Navigation;

/// <summary>将导航树 / 列表中的项滚动到可见区域</summary>
internal static class NavigationRevealHelper
{
    /// <summary>在 TreeView 中选中并滚动到指定项</summary>
    public static void RevealInTreeView(TreeView tree, object item)
    {
        tree.UpdateLayout();
        var container = FindTreeViewItem(tree, item);
        if (container == null)
        {
            tree.Dispatcher.BeginInvoke(() =>
            {
                var retry = FindTreeViewItem(tree, item);
                if (retry == null)
                {
                    return;
                }

                retry.IsSelected = true;
                retry.BringIntoView();
            }, System.Windows.Threading.DispatcherPriority.Loaded);

            return;
        }

        container.IsSelected = true;
        container.BringIntoView();
    }

    /// <summary>在 ListBox 中选中并滚动到指定项</summary>
    public static void RevealInListBox(ListBox list, object item)
    {
        list.UpdateLayout();
        list.SelectedItem = item;
        list.ScrollIntoView(item);
    }

    private static TreeViewItem? FindTreeViewItem(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem direct)
        {
            return direct;
        }

        foreach (var child in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(child) is not TreeViewItem childContainer)
            {
                continue;
            }

            if (child == item)
            {
                return childContainer;
            }

            var found = FindTreeViewItem(childContainer, item);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BiSheng.Latte.ViewModels;

namespace BiSheng.Latte.Controls.Navigation;

public partial class TreeNavigationPanel : UserControl
{
    private readonly ContextMenu _mergedTreeContextMenu = new();
    private readonly ContextMenu _favoritesListContextMenu = new();
    private MainViewModel? _subscribedVm;

    public TreeNavigationPanel()
    {
        InitializeComponent();
        MergedTree.ContextMenu = _mergedTreeContextMenu;
        FavoritesList.ContextMenu = _favoritesListContextMenu;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        _subscribedVm = vm;
        _subscribedVm.RevealActiveNoteRequested += OnRevealActiveNoteRequested;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribedVm == null)
        {
            return;
        }

        _subscribedVm.RevealActiveNoteRequested -= OnRevealActiveNoteRequested;
        _subscribedVm = null;
    }

    private void OnRevealActiveNoteRequested()
    {
        if (Vm == null)
        {
            return;
        }

        var note = Vm.Editor.CurrentNote;
        if (note == null)
        {
            return;
        }

        var noteItem = Vm.FolderTree.FindNoteInTree(Vm.FolderTree.RootNodes, note.Id);
        if (noteItem == null)
        {
            return;
        }

        NavigationRevealHelper.RevealInTreeView(MergedTree, noteItem);
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void MergedTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderNode folderNode)
            Vm.FolderTree.SelectedFolder = folderNode.Folder;
        else if (e.NewValue is NoteItemViewModel noteItem)
            NavigationRenameHelper.SelectNote(Vm, noteItem);
    }

    private void MergedTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (NavigationRenameHelper.IsClickOnInlineRenameTextBox(e.OriginalSource as DependencyObject))
            return;

        if (FindNoteItem(e.OriginalSource as DependencyObject) is { } noteItem)
        {
            NavigationRenameHelper.SelectNote(Vm, noteItem);
            return;
        }

        if (FindFolderNode(e.OriginalSource as DependencyObject) is not { } folderNode
            || folderNode.IsRenaming)
            return;

        if (e.ClickCount == 1)
        {
            folderNode.IsExpanded = !folderNode.IsExpanded;
            Vm.FolderTree.SelectedFolder = folderNode.Folder;
        }
    }

    private void MergedTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2) return;
        NavigationRenameHelper.HandleF2(Vm, MergedTree.SelectedItem, isTreeMode: true);
        e.Handled = true;
    }

    private void MergedTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var target = FindContextTarget(e.OriginalSource as DependencyObject);
        if (target is FolderNode fn)
            NavigationContextMenus.BuildFolderMenu(_mergedTreeContextMenu, Vm, fn);
        else if (target is NoteItemViewModel ni)
            NavigationContextMenus.BuildNoteMenu(_mergedTreeContextMenu, Vm, ni, useTreeRename: true);
        else
            NavigationContextMenus.BuildEmptyFolderMenu(_mergedTreeContextMenu, Vm);
    }

    private void FavoritesList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindFavoriteItem(e.OriginalSource as DependencyObject) is FolderNode fn)
            Vm.FolderTree.SelectedFolder = fn.Folder;
        else if (FindFavoriteItem(e.OriginalSource as DependencyObject) is NoteItemViewModel ni)
            NavigationRenameHelper.SelectNote(Vm, ni);
    }

    private void FavoritesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindFavoriteItem(e.OriginalSource as DependencyObject) is NoteItemViewModel ni)
            NavigationRenameHelper.SelectNote(Vm, ni);
    }

    private void FavoritesList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (FindFavoriteItem(e.OriginalSource as DependencyObject) is FolderNode fn)
            NavigationContextMenus.BuildFolderMenu(_favoritesListContextMenu, Vm, fn);
        else if (FindFavoriteItem(e.OriginalSource as DependencyObject) is NoteItemViewModel ni)
            NavigationContextMenus.BuildNoteMenu(_favoritesListContextMenu, Vm, ni, useTreeRename: true);
    }

    private void OnRenameTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
        => NavigationRenameHelper.OnRenameTextBoxPreviewKeyDown(Vm, sender, e);

    private void OnRenameTextBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        => NavigationRenameHelper.OnRenameTextBoxLostFocus(Vm, (TextBox)sender);

    private static object? FindContextTarget(DependencyObject? source)
    {
        var dep = source;
        while (dep != null && dep is not TreeViewItem)
            dep = VisualTreeHelper.GetParent(dep);
        return dep is FrameworkElement fe ? fe.DataContext : null;
    }

    private static FolderNode? FindFolderNode(DependencyObject? source)
    {
        var dep = source;
        while (dep != null && dep is not TreeViewItem)
            dep = VisualTreeHelper.GetParent(dep);
        return dep is FrameworkElement fe ? fe.DataContext as FolderNode : null;
    }

    private static NoteItemViewModel? FindNoteItem(DependencyObject? source)
    {
        var dep = source;
        while (dep != null && dep is not TreeViewItem)
            dep = VisualTreeHelper.GetParent(dep);
        return dep is FrameworkElement fe ? fe.DataContext as NoteItemViewModel : null;
    }

    private static object? FindFavoriteItem(DependencyObject? source)
    {
        var dep = source;
        while (dep != null && dep is not ListBoxItem)
            dep = VisualTreeHelper.GetParent(dep);
        return dep is FrameworkElement fe ? fe.DataContext : null;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BiSheng.Latte.ViewModels;

namespace BiSheng.Latte.Controls.Navigation;

public partial class SideBySideNavigationPanel : UserControl
{
    private readonly ContextMenu _folderTreeContextMenu = new();
    private readonly ContextMenu _noteListContextMenu = new();
    private readonly ContextMenu _favoritesListContextMenu = new();
    private MainViewModel? _subscribedVm;

    public SideBySideNavigationPanel()
    {
        InitializeComponent();
        FolderTree.ContextMenu = _folderTreeContextMenu;
        NoteList.ContextMenu = _noteListContextMenu;
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

        var folderNode = Vm.FolderTree.FindNodeById(Vm.FolderTree.RootNodes, note.FolderId);
        if (folderNode != null)
        {
            NavigationRevealHelper.RevealInTreeView(FolderTree, folderNode);
        }

        var noteItem = Vm.NoteList.Notes.FirstOrDefault(n => n.Id == note.Id);
        if (noteItem != null)
        {
            NavigationRevealHelper.RevealInListBox(NoteList, noteItem);
        }
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderNode node)
            Vm.FolderTree.SelectedFolder = node.Folder;
    }

    private void FolderTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindFolderNode(e.OriginalSource as DependencyObject) is not { } clickedNode)
            return;

        NavigationRenameHelper.HandleFolderPreviewClick(Vm, clickedNode, e, enableDoubleClickRename: true);
    }

    private void NoteList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindNoteItem(e.OriginalSource as DependencyObject) is not { } clickedItem)
            return;

        NavigationRenameHelper.HandleNotePreviewClick(Vm, clickedItem, e, enableDoubleClickRename: true);
    }

    private void NoteList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindNoteItem(e.OriginalSource as DependencyObject) is { } clickedItem)
            NavigationRenameHelper.HandleNotePreviewClick(Vm, clickedItem, e, enableDoubleClickRename: true);
    }

    private void FolderTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2) return;
        Vm.FolderTree.StartRenaming();
        e.Handled = true;
    }

    private void NoteList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2) return;
        Vm.NoteList.StartRenaming();
        e.Handled = true;
    }

    private void FolderTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (FindFolderNodeFromHit(e.OriginalSource as DependencyObject) is { } node)
            NavigationContextMenus.BuildFolderMenu(_folderTreeContextMenu, Vm, node);
        else
            NavigationContextMenus.BuildEmptyFolderMenu(_folderTreeContextMenu, Vm);
    }

    private void NoteList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (FindNoteItem(e.OriginalSource as DependencyObject) is { } note)
            NavigationContextMenus.BuildNoteMenu(_noteListContextMenu, Vm, note);
        else
            NavigationContextMenus.BuildEmptyNoteMenu(_noteListContextMenu, Vm);
    }

    private void FavoritesList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindFavoriteItem(e.OriginalSource as DependencyObject) is FolderNode fn)
            Vm.FolderTree.SelectedFolder = fn.Folder;
        else if (FindFavoriteItem(e.OriginalSource as DependencyObject) is NoteItemViewModel ni)
            NavigationRenameHelper.SelectNote(Vm, ni);
    }

    private void FavoritesList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (FindFavoriteItem(e.OriginalSource as DependencyObject) is FolderNode fn)
            NavigationContextMenus.BuildFolderMenu(_favoritesListContextMenu, Vm, fn);
        else if (FindFavoriteItem(e.OriginalSource as DependencyObject) is NoteItemViewModel ni)
            NavigationContextMenus.BuildNoteMenu(_favoritesListContextMenu, Vm, ni);
    }

    private void OnRenameTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
        => NavigationRenameHelper.OnRenameTextBoxPreviewKeyDown(Vm, sender, e);

    private void OnRenameTextBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        => NavigationRenameHelper.OnRenameTextBoxLostFocus(Vm, (TextBox)sender);

    private static FolderNode? FindFolderNode(DependencyObject? source)
    {
        var dep = source;
        while (dep != null && dep is not TreeViewItem)
            dep = VisualTreeHelper.GetParent(dep);
        return dep is FrameworkElement fe ? fe.DataContext as FolderNode : null;
    }

    private static FolderNode? FindFolderNodeFromHit(DependencyObject? source) => FindFolderNode(source);

    private static NoteItemViewModel? FindNoteItem(DependencyObject? source)
    {
        var dep = source;
        while (dep != null && dep is not ListBoxItem)
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

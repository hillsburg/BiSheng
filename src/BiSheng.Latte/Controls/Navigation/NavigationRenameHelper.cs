using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BiSheng.Latte.ViewModels;

namespace BiSheng.Latte.Controls.Navigation;

/// <summary>导航区内联重命名与树/列表交互</summary>
internal static class NavigationRenameHelper
{
    private static DispatcherTimer? _clickTimer;
    private static Guid? _clickTargetId;
    private static bool _clickTargetIsFolder;

    public static void OnRenameTextBoxPreviewKeyDown(MainViewModel vm, object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitFromTextBox(vm, (TextBox)sender);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelFromTextBox(vm, (TextBox)sender);
        }
    }

    public static void OnRenameTextBoxLostFocus(MainViewModel vm, TextBox textBox)
    {
        var folderNode = textBox.DataContext as FolderNode;
        var noteItem = textBox.DataContext as NoteItemViewModel;

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (folderNode != null && folderNode.IsRenaming)
                vm.FolderTree.CommitRename(folderNode);
            else if (noteItem != null && noteItem.IsRenaming)
                CommitNoteRename(vm, noteItem);
        }, DispatcherPriority.Background);
    }

    public static void CommitActiveRename(MainViewModel vm)
    {
        var folderNode = FindRenamingFolderNode(vm.FolderTree.RootNodes);
        if (folderNode != null)
        {
            vm.FolderTree.CommitRename(folderNode);
            return;
        }

        var noteItem = vm.NoteList.Notes.FirstOrDefault(n => n.IsRenaming);
        if (noteItem != null)
        {
            CommitNoteRename(vm, noteItem);
            return;
        }

        var mergedNote = FindRenamingNoteInTree(vm.FolderTree.RootNodes);
        if (mergedNote != null)
            vm.FolderTree.CommitNoteRename(mergedNote);
    }

    public static void HandleFolderPreviewClick(MainViewModel vm, FolderNode clickedNode, MouseButtonEventArgs e, bool enableDoubleClickRename)
    {
        if (clickedNode.IsRenaming) return;

        if (enableDoubleClickRename)
        {
            if (e.ClickCount >= 2)
            {
                BeginFolderRename(vm, clickedNode);
                return;
            }

            RegisterRenameClick(clickedNode.Id, isFolder: true, () => BeginFolderRename(vm, clickedNode));
        }
    }

    public static void HandleNotePreviewClick(MainViewModel vm, NoteItemViewModel clickedItem, MouseButtonEventArgs e, bool enableDoubleClickRename)
    {
        if (clickedItem.IsRenaming) return;

        if (enableDoubleClickRename)
        {
            if (e.ClickCount >= 2)
            {
                BeginNoteRename(vm, clickedItem);
                return;
            }

            RegisterRenameClick(clickedItem.Id, isFolder: false, () => BeginNoteRename(vm, clickedItem));
        }
    }

    public static void HandleF2(MainViewModel vm, object? selectedItem, bool isTreeMode)
    {
        if (selectedItem is FolderNode fn)
            vm.FolderTree.StartRenaming();
        else if (selectedItem is NoteItemViewModel ni)
        {
            if (isTreeMode)
                vm.FolderTree.StartRenamingNote(ni);
            else
                vm.NoteList.StartRenaming();
        }
    }

    public static void SelectNote(MainViewModel vm, NoteItemViewModel noteItem)
    {
        vm.NavigationStore.SelectNoteById(noteItem.Id, noteItem.FolderId);
    }

    public static bool IsClickOnInlineRenameTextBox(DependencyObject? source)
    {
        for (var dep = source; dep != null; dep = VisualTreeHelper.GetParent(dep))
        {
            if (dep is TextBox tb && tb.DataContext is FolderNode or NoteItemViewModel)
                return true;
        }

        return false;
    }

    private static void CommitFromTextBox(MainViewModel vm, TextBox textBox)
    {
        if (textBox.DataContext is FolderNode fn)
            vm.FolderTree.CommitRename(fn);
        else if (textBox.DataContext is NoteItemViewModel ni)
            CommitNoteRename(vm, ni);
    }

    private static void CancelFromTextBox(MainViewModel vm, TextBox textBox)
    {
        if (textBox.DataContext is FolderNode fn)
            vm.FolderTree.CancelRename(fn);
        else if (textBox.DataContext is NoteItemViewModel ni)
            CancelNoteRename(vm, ni);
    }

    private static void CommitNoteRename(MainViewModel vm, NoteItemViewModel item)
    {
        if (vm.IsTreeMode)
            vm.FolderTree.CommitNoteRename(item);
        else
            vm.NoteList.CommitRename(item);
    }

    private static void CancelNoteRename(MainViewModel vm, NoteItemViewModel item)
    {
        if (vm.IsTreeMode)
            vm.FolderTree.CancelNoteRename(item);
        else
            vm.NoteList.CancelRename(item);
    }

    private static void BeginFolderRename(MainViewModel vm, FolderNode node)
    {
        _clickTimer?.Stop();
        _clickTargetId = null;
        vm.FolderTree.SelectedFolder = node.Folder;
        node.RenameText = node.Name;
        node.IsRenaming = true;
    }

    private static void BeginNoteRename(MainViewModel vm, NoteItemViewModel item)
    {
        _clickTimer?.Stop();
        _clickTargetId = null;
        vm.NoteList.SelectedNote = item;
        item.RenameText = item.Title;
        item.IsRenaming = true;
    }

    private static void RegisterRenameClick(Guid targetId, bool isFolder, Action beginRename)
    {
        if (_clickTargetId == targetId && _clickTargetIsFolder == isFolder && _clickTimer?.IsEnabled == true)
        {
            _clickTimer.Stop();
            _clickTargetId = null;
            beginRename();
            return;
        }

        _clickTimer?.Stop();
        _clickTargetId = targetId;
        _clickTargetIsFolder = isFolder;
        _clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer?.Stop();
            _clickTargetId = null;
        };
        _clickTimer.Start();
    }

    private static FolderNode? FindRenamingFolderNode(IEnumerable<object> nodes)
    {
        foreach (var obj in nodes)
        {
            if (obj is FolderNode node)
            {
                if (node.IsRenaming) return node;
                var found = FindRenamingFolderNode(node.Children);
                if (found != null) return found;
            }
        }

        return null;
    }

    private static NoteItemViewModel? FindRenamingNoteInTree(IEnumerable<object> nodes)
    {
        foreach (var obj in nodes)
        {
            if (obj is NoteItemViewModel ni && ni.IsRenaming) return ni;
            if (obj is FolderNode fn)
            {
                var found = FindRenamingNoteInTree(fn.Children);
                if (found != null) return found;
            }
        }

        return null;
    }
}

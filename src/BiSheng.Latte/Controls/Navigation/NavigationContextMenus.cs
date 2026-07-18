using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BiSheng.Latte.Models;
using BiSheng.Latte.Services;
using BiSheng.Latte.ViewModels;

namespace BiSheng.Latte.Controls.Navigation;

/// <summary>导航区右键菜单构建（收藏/置顶 + CRUD）</summary>
internal static class NavigationContextMenus
{
    public static void BuildFolderMenu(ContextMenu menu, MainViewModel vm, FolderNode node)
    {
        menu.Items.Clear();
        vm.FolderTree.SelectedFolder = node.Folder;

        menu.Items.Add(MenuItem("新建子文件夹", (_, _) => vm.FolderTree.CreateSubFolderCommand.Execute(null)));
        menu.Items.Add(MenuItem("新建同级文件夹", (_, _) => vm.FolderTree.CreateSiblingFolderCommand.Execute(null)));
        menu.Items.Add(MenuItem("重命名", (_, _) => vm.FolderTree.StartRenaming()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(node.IsFavorite ? "取消收藏" : "收藏", (_, _) => vm.FolderTree.ToggleFavoriteFolder(node)));
        menu.Items.Add(MenuItem(node.IsPinned ? "取消置顶" : "置顶", (_, _) => vm.FolderTree.TogglePinFolder(node)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("新建笔记", (_, _) => vm.NoteList.CreateNoteInFolder(node.Folder.Id)));
        menu.Items.Add(new Separator());

        var export = new MenuItem { Header = "导出" };
        export.Items.Add(MenuItem("导出为 Markdown", async (_, _) => await vm.FolderTree.ExportFolderAsMarkdownCommand.ExecuteAsync(null)));
        export.Items.Add(MenuItem("导出为 Word", async (_, _) => await vm.FolderTree.ExportFolderAsWordCommand.ExecuteAsync(null)));
        export.Items.Add(MenuItem("导出为 PDF", async (_, _) => await vm.FolderTree.ExportFolderAsPdfCommand.ExecuteAsync(null)));
        menu.Items.Add(export);
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("删除", (_, _) =>
        {
            if (AppDialog.ConfirmDanger($"确认删除文件夹【{node.Name}】及其下的所有内容？", "确认删除"))
            {
                vm.FolderTree.DeleteSelectedFolderCommand.Execute(null);
            }
        }, isDanger: true));
    }

    public static void BuildNoteMenu(ContextMenu menu, MainViewModel vm, NoteItemViewModel note, bool useTreeRename = false)
    {
        menu.Items.Clear();
        vm.NoteList.SelectedNote = note;

        menu.Items.Add(MenuItem("重命名", (_, _) =>
        {
            if (useTreeRename)
                vm.FolderTree.StartRenamingNote(note);
            else
                vm.NoteList.StartRenaming();
        }));
        menu.Items.Add(MenuItem(note.IsFavorite ? "取消收藏" : "收藏", (_, _) => vm.NoteList.ToggleFavorite(note)));
        menu.Items.Add(MenuItem(note.IsPinned ? "取消置顶" : "置顶", (_, _) => vm.NoteList.TogglePin(note)));

        var export = new MenuItem { Header = "导出" };
        export.Items.Add(MenuItem("导出为 Markdown", async (_, _) => await vm.NoteList.ExportNoteAsMarkdownCommand.ExecuteAsync(null)));
        export.Items.Add(MenuItem("导出为 Word", async (_, _) => await vm.NoteList.ExportNoteAsWordCommand.ExecuteAsync(null)));
        export.Items.Add(MenuItem("导出为 PDF", async (_, _) => await vm.NoteList.ExportNoteAsPdfCommand.ExecuteAsync(null)));
        menu.Items.Add(export);
        menu.Items.Add(MenuItem("详情", (_, _) => vm.NoteList.ShowNoteDetailsCommand.Execute(null)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("删除", (_, _) =>
        {
            if (AppDialog.ConfirmDanger($"确认删除笔记【{note.Title}】？", "确认删除"))
            {
                vm.NoteList.DeleteSelectedNoteCommand.Execute(null);
            }
        }, isDanger: true));
    }

    public static void BuildEmptyFolderMenu(ContextMenu menu, MainViewModel vm)
    {
        menu.Items.Clear();
        menu.Items.Add(MenuItem("新建文件夹", (_, _) => vm.FolderTree.CreateFolderCommand.Execute(null)));
    }

    public static void BuildEmptyNoteMenu(ContextMenu menu, MainViewModel vm)
    {
        menu.Items.Clear();
        if (vm.FolderTree.SelectedFolder != null)
        {
            menu.Items.Add(MenuItem("新建笔记", (_, _) =>
                vm.NoteList.CreateNoteInFolder(vm.FolderTree.SelectedFolder!.Id)));
        }
        else
        {
            menu.Items.Add(MenuItem("请先选择文件夹", (_, _) => { }, isEnabled: false));
        }
    }

    private static MenuItem MenuItem(string header, RoutedEventHandler handler, bool isDanger = false, bool isEnabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = isEnabled };
        if (isDanger)
        {
            item.SetResourceReference(Control.ForegroundProperty, ThemeBrushKeys.Danger);
        }

        if (isEnabled)
        {
            item.Click += handler;
        }

        return item;
    }
}

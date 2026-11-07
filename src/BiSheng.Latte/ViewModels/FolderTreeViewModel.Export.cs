using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BiSheng.Latte.ViewModels;

/// <summary>
/// 文件夹树 · 导出：把选中文件夹（含子文件夹）导出为 Markdown / Word / PDF。
/// 未注入 ExportService（如测试环境）时导出命令为空操作。
/// </summary>
public partial class FolderTreeViewModel
{
    /// <summary>
    /// 将选中文件夹导出为 Markdown 文件，弹出目录选择对话框。
    /// </summary>
    [RelayCommand]
    private Task ExportFolderAsMarkdownAsync()
    {
        return ExportSelectedFolderAsync("选择 Markdown 导出目录",
            (service, folder, dir) => service.ExportFolderAsMarkdownAsync(folder, dir));
    }

    /// <summary>
    /// 将选中文件夹导出为 Word 文件，弹出目录选择对话框。
    /// </summary>
    [RelayCommand]
    private Task ExportFolderAsWordAsync()
    {
        return ExportSelectedFolderAsync("选择 Word 导出目录",
            (service, folder, dir) => service.ExportFolderAsWordAsync(folder, dir));
    }

    /// <summary>
    /// 将选中文件夹导出为 PDF 文件，弹出目录选择对话框。
    /// </summary>
    [RelayCommand]
    private Task ExportFolderAsPdfAsync()
    {
        return ExportSelectedFolderAsync("选择 PDF 导出目录",
            (service, folder, dir) => service.ExportFolderAsPdfAsync(folder, dir));
    }

    /// <summary>
    /// 导出公共流程：选目录 → 导出 → 提示结果。异常在此弹窗兜底，不向命令外抛出。
    /// </summary>
    private async Task ExportSelectedFolderAsync(
        string dialogTitle,
        Func<ExportService, LocalFolder, string, Task> export)
    {
        if (SelectedFolder == null || _exportService == null)
        {
            return;
        }

        var dialog = new OpenFolderDialog { Title = dialogTitle };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await export(_exportService, SelectedFolder, dialog.FolderName);
            AppDialog.Success("文件夹已导出到:\n" + dialog.FolderName, "导出成功");
        }
        catch (Exception ex)
        {
            AppDialog.Error("导出失败: " + ex.Message, "导出失败");
        }
    }
}

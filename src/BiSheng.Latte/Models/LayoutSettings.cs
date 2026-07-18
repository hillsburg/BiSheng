using System.IO;
using System.Text.Json;

namespace BiSheng.Latte.Models;

/// <summary>
/// 界面布局设置模型：面板显隐、列宽、文件夹树展开状态
/// 持久化为 JSON 文件，下次启动时恢复上次关闭时的布局
/// </summary>
public class LayoutSettings
{
    // ===== 面板显隐 =====

    /// <summary>文件夹树面板是否可见</summary>
    public bool FolderPanelVisible { get; set; } = true;

    /// <summary>笔记列表面板是否可见</summary>
    public bool NotePanelVisible { get; set; } = true;

    /// <summary>大纲面板是否可见</summary>
    public bool OutlinePanelVisible { get; set; } = false;

    // ===== 列宽（GridSplitter 拖动后的值） =====

    /// <summary>文件夹树列宽（默认 220）</summary>
    public double FolderColumnWidth { get; set; } = 220;

    /// <summary>笔记列表列宽（默认 280）</summary>
    public double NoteColumnWidth { get; set; } = 280;

    /// <summary>大纲列宽（默认 240）</summary>
    public double OutlineColumnWidth { get; set; } = 240;

    /// <summary>归纳模式下合并树列宽（默认 280）</summary>
    public double MergedTreeColumnWidth { get; set; } = 280;

    // ===== 文件夹树展开状态 =====

    /// <summary>上次关闭时展开的文件夹 ID 列表</summary>
    public List<string> ExpandedFolderIds { get; set; } = new();

    // ===== 上次编辑的笔记 =====

    /// <summary>上次编辑的笔记所属文件夹 ID</summary>
    public string? LastFolderId { get; set; }

    /// <summary>上次编辑的笔记 ID</summary>
    public string? LastNoteId { get; set; }

    // ===== 持久化 =====

    private static string SettingsPath =>
        Path.Combine(Services.LatteAppPaths.Root, "layout.json");

    /// <summary>
    /// 从磁盘加载设置（文件不存在则返回默认值）
    /// </summary>
    public static LayoutSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<LayoutSettings>(json) ?? new LayoutSettings();
            }
        }
        catch { /* 解析失败则返回默认值 */ }
        return new LayoutSettings();
    }

    /// <summary>
    /// 保存设置到磁盘
    /// </summary>
    public void Save()
    {
        var path = SettingsPath;
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}

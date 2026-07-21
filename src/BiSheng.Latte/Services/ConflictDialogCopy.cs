using BiSheng.Shared;

namespace BiSheng.Latte.Services;

/// <summary>冲突对话框文案（操作类型、删除场景按钮）</summary>
public static class ConflictDialogCopy
{
    /// <summary>将 Create/Update/Delete 转为中文短标签</summary>
    public static string FormatAction(string? action) => action switch
    {
        ChangeActions.Create => "创建",
        ChangeActions.Update => "更新",
        ChangeActions.Delete => "删除",
        _ => string.IsNullOrWhiteSpace(action) ? "未知" : action
    };

    /// <summary>「保留本地」按钮文案</summary>
    public static string KeepLocalButton(string? localAction) =>
        localAction == ChangeActions.Delete ? "保留本机删除" : "保留本地版本";

    /// <summary>「保留远端」按钮文案</summary>
    public static string KeepRemoteButton(string? localAction, string? remoteAction)
    {
        if (remoteAction == ChangeActions.Delete)
        {
            return "采用远端删除";
        }

        if (localAction == ChangeActions.Delete)
        {
            return "恢复远端内容";
        }

        return "保留远端版本";
    }

    /// <summary>页眉操作对照说明</summary>
    public static string FormatActionPair(string? localAction, string? remoteAction) =>
        $"本地：{FormatAction(localAction)} · 远端：{FormatAction(remoteAction)}";
}

using BiSheng.Latte.Services;

namespace BiSheng.Latte.Models;

/// <summary>工具栏 / 底栏 / 设置页统一的连接与同步健康度</summary>
public enum ConnectionDisplayState
{
    /// <summary>未配置服务器凭据</summary>
    NotConfigured,

    /// <summary>离线模式</summary>
    Offline,

    /// <summary>凭据已保存但用户关闭同步</summary>
    SyncDisabled,

    /// <summary>凭据完整，尚未完成连通性验证</summary>
    PendingVerify,

    /// <summary>无法连接服务器</summary>
    CannotConnect,

    /// <summary>正在推送或拉取</summary>
    Syncing,

    /// <summary>同步出错，待重试</summary>
    SyncError,

    /// <summary>存在未解决冲突</summary>
    Conflict,

    /// <summary>已连通，但本地仍有待推送变更</summary>
    PendingPush,

    /// <summary>已验证且处于空闲/实时连接，本地无待推送</summary>
    Synced,
}

/// <summary>连接状态展示模型：短标签、底栏文案、详情、主题 Brush 键与图标</summary>
public sealed class ConnectionDisplayInfo
{
    /// <summary>当前状态</summary>
    public ConnectionDisplayState State { get; init; }

    /// <summary>工具栏短标签</summary>
    public string ShortLabel { get; init; } = "—";

    /// <summary>底栏短文案（可含引擎活动说明）</summary>
    public string StatusBarText { get; init; } = "—";

    /// <summary>Tooltip / 设置页详情</summary>
    public string DetailText { get; init; } = "";

    /// <summary>DynamicResource 键（Brush.Success 等）</summary>
    public string BrushKey { get; init; } = ThemeBrushKeys.TextMuted;

    /// <summary>Segoe MDL2 Assets 图标</summary>
    public string IconGlyph { get; init; } = "\uE946";

    /// <summary>点击入口是否打开冲突解决（否则打开同步设置）</summary>
    public bool OpensConflicts { get; init; }

    /// <summary>默认占位</summary>
    public static ConnectionDisplayInfo Default { get; } = new()
    {
        State = ConnectionDisplayState.NotConfigured,
        ShortLabel = "未配置",
        StatusBarText = "未配置服务器",
        DetailText = "未配置服务器，笔记仅保存在本机。",
        BrushKey = ThemeBrushKeys.TextMuted,
        IconGlyph = "\uE703",
        OpensConflicts = false,
    };
}

/// <summary>根据认证与同步引擎状态解析统一展示信息</summary>
public static class ConnectionDisplayResolver
{
    /// <summary>解析当前应对用户展示的状态</summary>
    /// <param name="auth">认证服务</param>
    /// <param name="syncStatus">同步引擎状态</param>
    /// <param name="hasConflicts">是否有未解决冲突</param>
    /// <param name="conflictCount">冲突数量</param>
    /// <param name="activityMessage">引擎最近活动文案（推送中/错误详情等，可空）</param>
    /// <param name="pendingChangeCount">本地待推送变更条数</param>
    public static ConnectionDisplayInfo Resolve(
        AuthService auth,
        SyncStatus syncStatus,
        bool hasConflicts,
        int conflictCount = 0,
        string? activityMessage = null,
        int pendingChangeCount = 0)
    {
        if (hasConflicts && conflictCount > 0)
        {
            var label = conflictCount > 1 ? $"冲突 ({conflictCount})" : "冲突";
            return Create(
                ConnectionDisplayState.Conflict,
                label,
                $"{label} · 点击处理",
                $"有 {conflictCount} 处同步冲突待处理。点击连接状态可打开冲突解决。",
                ThemeBrushKeys.Danger,
                "\uE7BA",
                auth,
                opensConflicts: true);
        }

        if (auth.IsOfflineMode)
        {
            return Create(
                ConnectionDisplayState.Offline,
                "离线",
                PreferActivity(activityMessage, "离线模式"),
                "未配置完整服务器凭据，笔记仅保存在本机。",
                ThemeBrushKeys.TextMuted,
                "\uE709",
                auth);
        }

        if (auth.HasCredentials && !auth.IsSyncEnabled)
        {
            return Create(
                ConnectionDisplayState.SyncDisabled,
                "同步已关闭",
                PreferActivity(activityMessage, "同步已关闭"),
                "凭据已保留，同步功能已关闭。可在「同步与安全 → 连接」重新启用。",
                ThemeBrushKeys.Accent,
                "\uE769",
                auth);
        }

        if (!auth.HasCredentials)
        {
            return Create(
                ConnectionDisplayState.NotConfigured,
                "未配置",
                PreferActivity(activityMessage, "未配置服务器"),
                "尚未配置服务器地址与 API Key。",
                ThemeBrushKeys.TextMuted,
                "\uE703",
                auth);
        }

        // 引擎正在 Push/Pull：优先展示同步中（即使此前验证失败）
        if (syncStatus is SyncStatus.Pushing or SyncStatus.Pulling)
        {
            var fallback = syncStatus == SyncStatus.Pushing ? "正在推送…" : "正在拉取…";
            return Create(
                ConnectionDisplayState.Syncing,
                "同步中",
                PreferActivity(activityMessage, fallback),
                syncStatus == SyncStatus.Pushing
                    ? "正在将本地变更推送到服务器…"
                    : "正在从服务器拉取变更…",
                ThemeBrushKeys.Accent,
                "\uE895",
                auth);
        }

        if (auth.IsServerVerified == false && syncStatus != SyncStatus.Connected)
        {
            return Create(
                ConnectionDisplayState.CannotConnect,
                "无法连接",
                PreferActivity(activityMessage, "无法连接服务器"),
                "无法连接服务器，请检查地址、API Key 或网络。",
                ThemeBrushKeys.Danger,
                "\uE783",
                auth);
        }

        if (syncStatus == SyncStatus.Error)
        {
            var errorDetail = pendingChangeCount > 0
                ? $"最近一次同步失败，本地仍有 {pendingChangeCount} 条未推送，将在网络恢复或下次轮询时重试。"
                : "最近一次同步失败，将在网络恢复或下次轮询时重试。";
            return Create(
                ConnectionDisplayState.SyncError,
                "待重试",
                PreferActivity(activityMessage, "同步失败，待重试"),
                errorDetail,
                ThemeBrushKeys.Danger,
                "\uE946",
                auth);
        }

        // 最近一次 Push/Pull 已成功，或已验证连通 → 勿被陈旧的 IsServerVerified=false 盖住
        if (syncStatus == SyncStatus.Connected || auth.IsServerVerified == true)
        {
            if (pendingChangeCount > 0)
            {
                var shortLabel = pendingChangeCount > 1
                    ? $"有未推送 ({pendingChangeCount})"
                    : "有未推送";
                return Create(
                    ConnectionDisplayState.PendingPush,
                    shortLabel,
                    PreferActivity(activityMessage, $"有 {pendingChangeCount} 条未推送"),
                    $"本地还有 {pendingChangeCount} 条变更尚未推送到服务器，将自动重试。当前不代表已全部上云。",
                    ThemeBrushKeys.Accent,
                    "\uE898",
                    auth);
            }

            return Create(
                ConnectionDisplayState.Synced,
                "已同步",
                PreferActivity(activityMessage, "已同步"),
                "已与服务器连接，本地无待推送变更。",
                ThemeBrushKeys.Success,
                "\uE73E",
                auth);
        }

        return Create(
            ConnectionDisplayState.PendingVerify,
            "待验证",
            PreferActivity(activityMessage, "等待连接验证"),
            "凭据已保存，正在等待连接验证。",
            ThemeBrushKeys.Accent,
            "\uE895",
            auth);
    }

    /// <summary>有活动文案时优先用于底栏；否则用回退短句</summary>
    private static string PreferActivity(string? activityMessage, string fallback)
    {
        if (string.IsNullOrWhiteSpace(activityMessage))
        {
            return fallback;
        }

        return activityMessage.Trim();
    }

    private static ConnectionDisplayInfo Create(
        ConnectionDisplayState state,
        string shortLabel,
        string statusBarText,
        string detail,
        string brushKey,
        string icon,
        AuthService auth,
        bool opensConflicts = false)
    {
        return new ConnectionDisplayInfo
        {
            State = state,
            ShortLabel = shortLabel,
            StatusBarText = statusBarText,
            DetailText = AppendAccountDetail(detail, auth),
            BrushKey = brushKey,
            IconGlyph = icon,
            OpensConflicts = opensConflicts,
        };
    }

    private static string AppendAccountDetail(string detail, AuthService auth)
    {
        if (!auth.HasCredentials)
        {
            return detail;
        }

        var user = auth.Username ?? "—";
        var server = auth.ServerUrl ?? "—";
        return $"{detail}\n\n用户：{user}\n服务器：{server}";
    }
}

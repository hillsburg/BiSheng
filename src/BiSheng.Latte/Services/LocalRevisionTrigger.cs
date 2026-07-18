namespace BiSheng.Latte.Services;

/// <summary>本地历史快照触发原因</summary>
public enum LocalRevisionTrigger
{
    /// <summary>用户手动「保存当前版本」</summary>
    Manual,

    /// <summary>切换到其他笔记前</summary>
    NoteSwitch,

    /// <summary>停笔空闲达到 <see cref="BiSheng.Shared.LocalRevisionPolicy.IdleSnapshotMinutes"/> 分钟</summary>
    Idle,

    /// <summary>从历史版本恢复后</summary>
    Restore,

    /// <summary>应用退出前</summary>
    AppExit
}

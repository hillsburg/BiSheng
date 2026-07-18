using BiSheng.Latte.Models;
using BiSheng.Shared;

namespace BiSheng.Latte.Services;

/// <summary>
/// 本地历史快照采样器：判断一次编辑是否值得写入 <see cref="Data.Entities.LocalNoteRevision"/>。
/// 与 800ms 自动保存解耦，避免标点级修改产生历史版本。
/// </summary>
internal static class LocalRevisionSampler
{
    /// <summary>
    /// 是否应记录一条新的本地历史快照。
    /// </summary>
    /// <param name="trigger">触发来源（手动/恢复不受「微小改动」限制）</param>
    /// <param name="title">当前标题</param>
    /// <param name="content">当前正文</param>
    /// <param name="latestTitle">上一条历史的标题</param>
    /// <param name="latestContent">上一条历史的正文</param>
    /// <param name="latestHash">上一条历史的 ContentHash</param>
    /// <param name="latestCreatedAt">上一条历史的创建时间（用于自动快照间隔）</param>
    /// <returns>通过采样策略时返回 true</returns>
    public static bool ShouldRecord(
        LocalRevisionTrigger trigger,
        string title,
        string content,
        string? latestTitle,
        string? latestContent,
        string? latestHash,
        DateTime? latestCreatedAt)
    {
        var hash = NoteContentHash.Compute(title, content);

        // 与上一版完全相同，无需重复快照
        if (latestHash == hash)
        {
            return false;
        }

        // 用户主动保存或恢复：仅 hash 去重
        if (trigger is LocalRevisionTrigger.Manual or LocalRevisionTrigger.Restore)
        {
            return true;
        }

        var safety = DataSafetySettings.Load();
        var minCharDelta = EffectiveRevisionPolicy.MinCharDelta(safety);
        var minLineDelta = EffectiveRevisionPolicy.MinLineDelta(safety);

        // 切换笔记：有意义改动即可，不受最短间隔约束（保留切换点快照）
        if (trigger == LocalRevisionTrigger.NoteSwitch)
        {
            return NoteRevisionSampling.IsSignificantChange(
                latestTitle,
                latestContent,
                title,
                content,
                minCharDelta,
                minLineDelta);
        }

        // 空闲 / 退出：与服务端 Push 历史同一套 Shared 门槛（档位可收紧间隔与改动量）
        return NoteRevisionSampling.ShouldRecordAuto(
            title,
            content,
            latestTitle,
            latestContent,
            latestHash,
            latestCreatedAt,
            DateTime.UtcNow,
            EffectiveRevisionPolicy.MinAutoIntervalMinutes(safety),
            minCharDelta,
            minLineDelta);
    }
}

using System;

namespace BiSheng.Shared;

/// <summary>
/// 笔记历史自动快照采样：服务端 Push 写入与客户端空闲/退出类快照共用门槛。
/// 手动保存、用户主动恢复等场景可跳过间隔与「微小改动」判定，仅 hash 去重。
/// </summary>
public static class NoteRevisionSampling
{
    /// <summary>
    /// 相对上一版是否应写入自动快照（hash / 微小改动 / 最短间隔）。
    /// </summary>
    /// <param name="title">当前标题</param>
    /// <param name="content">当前正文</param>
    /// <param name="latestTitle">上一版标题</param>
    /// <param name="latestContent">上一版正文</param>
    /// <param name="latestHash">上一版 ContentHash</param>
    /// <param name="latestCreatedAt">上一版创建时间（UTC）</param>
    /// <param name="nowUtc">判定时刻（UTC）</param>
    /// <param name="minIntervalMinutes">两次自动快照最短间隔（分钟）</param>
    /// <param name="minCharDelta">有意义字数变化阈值</param>
    /// <param name="minLineDelta">有意义行数变化阈值</param>
    public static bool ShouldRecordAuto(
        string title,
        string content,
        string? latestTitle,
        string? latestContent,
        string? latestHash,
        DateTime? latestCreatedAt,
        DateTime nowUtc,
        int minIntervalMinutes = LocalRevisionPolicy.MinAutoIntervalMinutes,
        int minCharDelta = LocalRevisionPolicy.MinCharDelta,
        int minLineDelta = LocalRevisionPolicy.MinLineDelta)
    {
        var hash = NoteContentHash.Compute(title, content);

        if (latestHash == hash)
        {
            return false;
        }

        if (!IsSignificantChange(latestTitle, latestContent, title, content, minCharDelta, minLineDelta))
        {
            return false;
        }

        if (latestCreatedAt.HasValue &&
            nowUtc - latestCreatedAt.Value < TimeSpan.FromMinutes(minIntervalMinutes))
        {
            return false;
        }

        return true;
    }

    /// <summary>相对上一版是否为有意义改动（标题变化、字数或行数达标）</summary>
    public static bool IsSignificantChange(
        string? oldTitle,
        string? oldContent,
        string newTitle,
        string newContent,
        int minCharDelta = LocalRevisionPolicy.MinCharDelta,
        int minLineDelta = LocalRevisionPolicy.MinLineDelta)
    {
        if ((oldTitle ?? string.Empty) != newTitle)
        {
            return true;
        }

        var old = oldContent ?? string.Empty;
        var @new = newContent ?? string.Empty;
        if (old == @new)
        {
            return false;
        }

        if (Math.Abs(@new.Length - old.Length) >= minCharDelta)
        {
            return true;
        }

        return Math.Abs(CountLines(@new) - CountLines(old)) >= minLineDelta;
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return text.Split('\n').Length;
    }
}

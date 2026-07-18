using BiSheng.Shared;

namespace BiSheng.Server.Tests.Mutations;

/// <summary>服务端 / 客户端共用的自动历史采样门槛</summary>
public class NoteRevisionSamplingTests
{
    /// <summary>首次写入（无上一版）应允许</summary>
    [Fact]
    public void ShouldRecordAuto_FirstRevision_Allowed()
    {
        var ok = NoteRevisionSampling.ShouldRecordAuto(
            "t",
            new string('a', 40),
            latestTitle: null,
            latestContent: null,
            latestHash: null,
            latestCreatedAt: null,
            nowUtc: DateTime.UtcNow);

        Assert.True(ok);
    }

    /// <summary>字数变化不足且行数不足时拒绝</summary>
    [Fact]
    public void ShouldRecordAuto_TinyEdit_Rejected()
    {
        var oldContent = "hello world";
        var newContent = "hello world!";
        var hash = NoteContentHash.Compute("t", oldContent);

        var ok = NoteRevisionSampling.ShouldRecordAuto(
            "t",
            newContent,
            "t",
            oldContent,
            hash,
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow);

        Assert.False(ok);
    }

    /// <summary>有意义改动但未满最短间隔时拒绝</summary>
    [Fact]
    public void ShouldRecordAuto_WithinMinInterval_Rejected()
    {
        var oldContent = new string('a', 10);
        var newContent = new string('b', 50);
        var hash = NoteContentHash.Compute("t", oldContent);
        var now = DateTime.UtcNow;

        var ok = NoteRevisionSampling.ShouldRecordAuto(
            "t",
            newContent,
            "t",
            oldContent,
            hash,
            latestCreatedAt: now.AddMinutes(-3),
            nowUtc: now);

        Assert.False(ok);
    }

    /// <summary>有意义改动且已过最短间隔时允许</summary>
    [Fact]
    public void ShouldRecordAuto_SignificantAndIntervalElapsed_Allowed()
    {
        var oldContent = new string('a', 10);
        var newContent = new string('b', 50);
        var hash = NoteContentHash.Compute("t", oldContent);
        var now = DateTime.UtcNow;

        var ok = NoteRevisionSampling.ShouldRecordAuto(
            "t",
            newContent,
            "t",
            oldContent,
            hash,
            latestCreatedAt: now.AddMinutes(-(LocalRevisionPolicy.MinAutoIntervalMinutes + 1)),
            nowUtc: now);

        Assert.True(ok);
    }
}

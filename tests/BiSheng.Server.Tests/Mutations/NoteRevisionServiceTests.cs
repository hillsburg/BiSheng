using BiSheng.Server.Data.Entities;
using BiSheng.Server.Services;
using BiSheng.Server.Tests.Fixtures;
using BiSheng.Shared;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Tests.Mutations;

/// <summary>Push 附属写历史的采样行为</summary>
public class NoteRevisionServiceTests
{
    /// <summary>短间隔内连续 Push 有内容变化时，不重复写历史</summary>
    [Fact]
    public async Task RecordIfChanged_SkipsWithinMinInterval()
    {
        using var fixture = new TestDbFactory();
        var (userId, _, _, noteId) = fixture.SeedUserWithNote();
        var note = await fixture.Db.Notes.SingleAsync(n => n.Id == noteId);
        var svc = new NoteRevisionService();

        note.Content = new string('x', 60);
        note.Title = "rev1";
        await svc.RecordIfChangedAsync(fixture.Db, note, noteVersion: 11);
        await fixture.Db.SaveChangesAsync();

        note.Content = new string('y', 60);
        note.Title = "rev2";
        await svc.RecordIfChangedAsync(fixture.Db, note, noteVersion: 12);
        await fixture.Db.SaveChangesAsync();

        var count = await fixture.Db.NoteRevisions.CountAsync(r => r.NoteId == noteId);
        Assert.Equal(1, count);
    }

    /// <summary>force 时绕过间隔，只要 hash 不同即写入</summary>
    [Fact]
    public async Task RecordIfChanged_Force_BypassesInterval()
    {
        using var fixture = new TestDbFactory();
        var (_, _, _, noteId) = fixture.SeedUserWithNote();
        var note = await fixture.Db.Notes.SingleAsync(n => n.Id == noteId);
        var svc = new NoteRevisionService();

        note.Content = new string('x', 60);
        await svc.RecordIfChangedAsync(fixture.Db, note, 11);
        await fixture.Db.SaveChangesAsync();

        note.Content = new string('y', 60);
        await svc.RecordIfChangedAsync(fixture.Db, note, 12, force: true);
        await fixture.Db.SaveChangesAsync();

        var count = await fixture.Db.NoteRevisions.CountAsync(r => r.NoteId == noteId);
        Assert.Equal(2, count);
    }
}

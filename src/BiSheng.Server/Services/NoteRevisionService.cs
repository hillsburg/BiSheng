using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using BiSheng.Shared;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Services;

/// <summary>
/// 笔记历史版本服务：Push/REST 成功且通过采样门槛时写入快照。
/// 与 SyncLog 裁剪无关，每笔记最多保留 <see cref="NoteRevisionLimits.MaxPerNote"/> 条。
/// </summary>
public class NoteRevisionService
{
/// <summary>
/// 内容相对该笔记上一版历史有变化，且满足间隔与改动量门槛时写入快照，并 FIFO 裁剪。
/// 不阻断 Notes / SyncLog 的主变更提交。
/// </summary>
/// <param name="db">当前请求/事务内的 DbContext</param>
/// <param name="note">已更新后的笔记实体</param>
/// <param name="noteVersion">本次变更对应的全局同步 Version</param>
/// <param name="ct">取消标记</param>
/// <param name="force">true 时仅 hash 去重（恢复历史等显式操作）</param>
public async Task RecordIfChangedAsync(
    AppDbContext db,
    Note note,
    long noteVersion,
    CancellationToken ct = default,
    bool force = false)
{
    var latest = await db.NoteRevisions
        .Where(r => r.NoteId == note.Id && r.UserId == note.UserId)
        .OrderByDescending(r => r.RevisionNumber)
        .FirstOrDefaultAsync(ct);

    var hash = NoteContentHash.Compute(note.Title, note.Content);

    if (latest?.ContentHash == hash)
    {
        return;
    }

    if (!force &&
        !NoteRevisionSampling.ShouldRecordAuto(
            note.Title,
            note.Content,
            latest?.Title,
            latest?.Content,
            latest?.ContentHash,
            latest?.CreatedAt,
            DateTime.UtcNow))
    {
        return;
    }

    var nextNumber = (latest?.RevisionNumber ?? 0) + 1;
    db.NoteRevisions.Add(new NoteRevision
    {
        NoteId = note.Id,
        UserId = note.UserId,
        RevisionNumber = nextNumber,
        Title = note.Title,
        Content = note.Content,
        ContentHash = hash,
        NoteVersion = noteVersion,
        CreatedAt = DateTime.UtcNow
    });

    await TrimExcessAsync(db, note.Id, note.UserId, ct);
}

    /// <summary>删除超出上限的最旧历史记录</summary>
    public async Task TrimExcessAsync(
        AppDbContext db,
        Guid noteId,
        Guid userId,
        CancellationToken ct = default)
    {
        var count = await db.NoteRevisions.CountAsync(r => r.NoteId == noteId && r.UserId == userId, ct);
        if (count <= NoteRevisionLimits.MaxPerNote)
        {
            return;
        }

        var removeCount = count - NoteRevisionLimits.MaxPerNote;
        var toRemove = await db.NoteRevisions
            .Where(r => r.NoteId == noteId && r.UserId == userId)
            .OrderBy(r => r.RevisionNumber)
            .Take(removeCount)
            .ToListAsync(ct);

        db.NoteRevisions.RemoveRange(toRemove);
    }
}

using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Latte.Services;

/// <summary>
/// 笔记历史版本服务：本地快照持久化 + 服务端 REST 调用。
///
/// 职责边界：
/// - 本地历史经 <see cref="LocalRevisionSampler"/> 采样，不与自动保存绑定
/// - 服务端历史由 Push 成功后在服务端写入，本类仅负责拉取/恢复/删除 API
/// - 不经过 <see cref="LocalChangeTracker"/>，避免污染同步队列
/// </summary>
public class NoteRevisionService
{
    private readonly Func<LocalDbContext> _dbFactory;
    private readonly ApiClient _apiClient;
    private readonly AuthService _authService;

    public NoteRevisionService(
        Func<LocalDbContext> dbFactory,
        ApiClient apiClient,
        AuthService authService)
    {
        _dbFactory = dbFactory;
        _apiClient = apiClient;
        _authService = authService;
    }

    /// <summary>
    /// 按触发来源尝试写入一条本地历史；未通过采样或 hash 重复时返回 false。
    /// </summary>
    /// <param name="noteId">笔记 ID</param>
    /// <param name="title">快照标题</param>
    /// <param name="content">快照正文</param>
    /// <param name="trigger">触发原因</param>
    public bool TryRecordLocalRevision(
        Guid noteId,
        string title,
        string content,
        LocalRevisionTrigger trigger)
    {
        var hash = NoteContentHash.Compute(title, content);

        using var db = _dbFactory();
        var latest = db.NoteRevisions
            .Where(r => r.NoteId == noteId)
            .OrderByDescending(r => r.RevisionNumber)
            .FirstOrDefault();

        if (!LocalRevisionSampler.ShouldRecord(
                trigger,
                title,
                content,
                latest?.Title,
                latest?.Content,
                latest?.ContentHash,
                latest?.CreatedAt))
        {
            return false;
        }

        var nextNumber = (latest?.RevisionNumber ?? 0) + 1;
        db.NoteRevisions.Add(new LocalNoteRevision
        {
            NoteId = noteId,
            RevisionNumber = nextNumber,
            Title = title,
            Content = content,
            ContentHash = hash,
            CreatedAt = DateTime.UtcNow
        });

        TrimLocalExcess(db, noteId);
        db.SaveChangesWithLock();
        return true;
    }

    /// <summary>
    /// 用户手动「保存当前版本」：跳过微小改动判定，仍与上一版 hash 去重。
    /// </summary>
    public bool RecordManualLocalRevision(Guid noteId, string title, string content) =>
        TryRecordLocalRevision(noteId, title, content, LocalRevisionTrigger.Manual);

    /// <summary>获取指定笔记的本地历史列表（按 RevisionNumber 降序）</summary>
    public List<NoteRevisionListItemDto> GetLocalRevisionList(Guid noteId)
    {
        using var db = _dbFactory();
        return db.NoteRevisions
            .Where(r => r.NoteId == noteId)
            .OrderByDescending(r => r.RevisionNumber)
            .Select(r => new NoteRevisionListItemDto
            {
                Id = r.Id,
                NoteId = r.NoteId,
                RevisionNumber = r.RevisionNumber,
                Title = r.Title,
                ContentHash = r.ContentHash,
                CreatedAt = r.CreatedAt
            })
            .ToList();
    }

    /// <summary>获取单条本地历史详情</summary>
    public NoteRevisionDto? GetLocalRevision(Guid noteId, Guid revisionId)
    {
        using var db = _dbFactory();
        var r = db.NoteRevisions.FirstOrDefault(x => x.Id == revisionId && x.NoteId == noteId);
        if (r == null)
        {
            return null;
        }

        return new NoteRevisionDto
        {
            Id = r.Id,
            NoteId = r.NoteId,
            RevisionNumber = r.RevisionNumber,
            Title = r.Title,
            Content = r.Content,
            ContentHash = r.ContentHash,
            CreatedAt = r.CreatedAt
        };
    }

    /// <summary>从服务端拉取历史列表；离线或未连接时返回空列表</summary>
    public async Task<List<NoteRevisionListItemDto>> FetchServerRevisionListAsync(Guid noteId)
    {
        if (!_authService.IsConnected || _authService.IsOfflineMode || !_apiClient.CanUseApi)
        {
            return new List<NoteRevisionListItemDto>();
        }

        var result = await _apiClient.GetAsync<List<NoteRevisionListItemDto>>(
            $"/api/notes/{noteId}/revisions");
        return result ?? new List<NoteRevisionListItemDto>();
    }

    /// <summary>从服务端拉取单条历史详情</summary>
    public async Task<NoteRevisionDto?> FetchServerRevisionAsync(Guid noteId, Guid revisionId)
    {
        if (!_authService.IsConnected || _authService.IsOfflineMode || !_apiClient.CanUseApi)
        {
            return null;
        }

        return await _apiClient.GetAsync<NoteRevisionDto>(
            $"/api/notes/{noteId}/revisions/{revisionId}");
    }

    /// <summary>在服务端恢复指定历史版本（写回笔记并产生新 revision）</summary>
    public async Task<NoteRestoreResultDto?> RestoreOnServerAsync(Guid noteId, Guid revisionId)
    {
        if (!_authService.IsConnected || _authService.IsOfflineMode || !_apiClient.CanUseApi)
        {
            return null;
        }

        return await _apiClient.PostAsync<NoteRestoreResultDto>(
            $"/api/notes/{noteId}/revisions/{revisionId}/restore",
            new { });
    }

    /// <summary>删除服务端单条历史</summary>
    public async Task DeleteServerRevisionAsync(Guid noteId, Guid revisionId)
    {
        if (!_authService.IsConnected || _authService.IsOfflineMode || !_apiClient.CanUseApi)
        {
            return;
        }

        await _apiClient.DeleteAsync($"/api/notes/{noteId}/revisions/{revisionId}");
    }

    /// <summary>清空服务端该笔记的全部历史</summary>
    public async Task DeleteAllServerRevisionsAsync(Guid noteId)
    {
        if (!_authService.IsConnected || _authService.IsOfflineMode || !_apiClient.CanUseApi)
        {
            return;
        }

        await _apiClient.DeleteAsync($"/api/notes/{noteId}/revisions");
    }

    /// <summary>删除本地单条历史</summary>
    public void DeleteLocalRevision(Guid noteId, Guid revisionId)
    {
        using var db = _dbFactory();
        var r = db.NoteRevisions.FirstOrDefault(x => x.Id == revisionId && x.NoteId == noteId);
        if (r == null)
        {
            return;
        }

        db.NoteRevisions.Remove(r);
        db.SaveChangesWithLock();
    }

    /// <summary>清空本地该笔记的全部历史</summary>
    public void DeleteAllLocalRevisions(Guid noteId)
    {
        using var db = _dbFactory();
        var items = db.NoteRevisions.Where(r => r.NoteId == noteId).ToList();
        db.NoteRevisions.RemoveRange(items);
        db.SaveChangesWithLock();
    }

    /// <summary>FIFO 裁剪：超出 <see cref="NoteRevisionLimits.MaxPerNote"/> 时删除最旧记录</summary>
    private static void TrimLocalExcess(LocalDbContext db, Guid noteId)
    {
        var count = db.NoteRevisions.Count(r => r.NoteId == noteId);
        if (count <= NoteRevisionLimits.MaxPerNote)
        {
            return;
        }

        var removeCount = count - NoteRevisionLimits.MaxPerNote;
        var toRemove = db.NoteRevisions
            .Where(r => r.NoteId == noteId)
            .OrderBy(r => r.RevisionNumber)
            .Take(removeCount)
            .ToList();

        db.NoteRevisions.RemoveRange(toRemove);
    }
}

using BiSheng.Server.DTOs;

namespace BiSheng.Server.Services.Mutations;

/// <summary>笔记 REST 写路径：事务 + Writer + Notify</summary>
public interface INoteMutationService
{
    /// <summary>创建笔记</summary>
    Task<NoteMutationResult> CreateAsync(
        Guid userId,
        CreateNoteRequest request,
        CancellationToken ct = default);

    /// <summary>更新笔记</summary>
    Task<NoteMutationResult> UpdateAsync(
        Guid userId,
        Guid noteId,
        UpdateNoteRequest request,
        CancellationToken ct = default);

    /// <summary>软删除笔记</summary>
    Task<NoteMutationResult> DeleteAsync(
        Guid userId,
        Guid noteId,
        CancellationToken ct = default);

    /// <summary>将历史版本写回当前笔记（Update + SyncLog + Revision + Notify）</summary>
    Task<NoteMutationResult> RestoreFromRevisionAsync(
        Guid userId,
        Guid noteId,
        Guid revisionId,
        CancellationToken ct = default);
}

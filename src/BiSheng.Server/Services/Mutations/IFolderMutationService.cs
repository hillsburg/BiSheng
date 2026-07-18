using BiSheng.Server.DTOs;

namespace BiSheng.Server.Services.Mutations;

/// <summary>文件夹 REST 写路径：事务 + Writer + Notify</summary>
public interface IFolderMutationService
{
    /// <summary>创建文件夹</summary>
    Task<FolderMutationResult> CreateAsync(
        Guid userId,
        CreateFolderRequest request,
        CancellationToken ct = default);

    /// <summary>更新文件夹</summary>
    Task<FolderMutationResult> UpdateAsync(
        Guid userId,
        Guid folderId,
        UpdateFolderRequest request,
        CancellationToken ct = default);

    /// <summary>软删除文件夹（含级联子孙）</summary>
    Task<FolderMutationResult> DeleteAsync(
        Guid userId,
        Guid folderId,
        CancellationToken ct = default);
}

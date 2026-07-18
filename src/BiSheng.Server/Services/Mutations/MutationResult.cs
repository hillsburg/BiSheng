using BiSheng.Server.DTOs;

namespace BiSheng.Server.Services.Mutations;

/// <summary>REST 变更操作结果类型</summary>
public enum MutationOutcome
{
    /// <summary>成功</summary>
    Success,

    /// <summary>资源不存在</summary>
    NotFound,

    /// <summary>请求无效</summary>
    BadRequest,

    /// <summary>服务端内部错误</summary>
    InternalError
}

/// <summary>笔记变更结果</summary>
public sealed record NoteMutationResult
{
    /// <summary>结果类型</summary>
    public MutationOutcome Outcome { get; init; }

    /// <summary>创建成功时的笔记 DTO</summary>
    public NoteDto? Note { get; init; }

    /// <summary>失败时的错误信息</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>文件夹变更结果</summary>
public sealed record FolderMutationResult
{
    /// <summary>结果类型</summary>
    public MutationOutcome Outcome { get; init; }

    /// <summary>创建成功时的文件夹 DTO</summary>
    public FolderDto? Folder { get; init; }

    /// <summary>失败时的错误信息</summary>
    public string? ErrorMessage { get; init; }
}

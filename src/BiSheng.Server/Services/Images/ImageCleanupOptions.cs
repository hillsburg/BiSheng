namespace BiSheng.Server.Services.Images;

/// <summary>
/// 图片 GC 可调选项（绑定配置节 <c>ImageGc</c>，无需改库）
/// </summary>
public class ImageCleanupOptions
{
    /// <summary>配置节名称</summary>
    public const string SectionName = "ImageGc";

    /// <summary>
    /// 上传后超过该天数仍未被任何笔记引用，则视为孤儿并软删除（默认 7）
    /// </summary>
    public int OrphanGraceDays { get; set; } = 7;

    /// <summary>
    /// 为 true 时仅记录将清理的孤儿，不写库（首次上线建议先开 dry-run）
    /// </summary>
    public bool OrphanDryRun { get; set; }
}

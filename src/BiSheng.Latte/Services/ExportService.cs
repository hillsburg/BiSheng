using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig;

namespace BiSheng.Latte.Services;

/// <summary>
/// 导出服务：将笔记和文件夹导出为 Markdown / Word / PDF 格式
///
/// 导出规则：
/// - 顶层导出目标一律带本地时间戳（yyyyMMdd_HHmmss），避免覆盖历史导出
/// - 单篇笔记 Markdown：{标题}_{时间戳}/，内含 {标题}.md + img/
/// - 单篇笔记 Word/PDF：默认文件名为 {标题}_{时间戳}.docx|.pdf
/// - 文件夹 / 全库：根目录带时间戳；内部条目不再重复加戳，同名时自动追加序号
/// </summary>
public class ExportService
{
    private readonly ImageStorageService _imageStorage;
    private readonly Func<LocalDbContext> _dbFactory;
    private readonly WebView2PdfExportHost _pdfExportHost;
    private static readonly HttpClient _httpClient = new();

    private const string BishengImagePrefix = "bisheng://img/";

    // 匹配 Markdown 图片引用：![alt](url) 和 <img src="url">
    private static readonly Regex MdImageRegex = new(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex HtmlImgRegex = new(@"<img[^>]+src\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ExportService(
        ImageStorageService imageStorage,
        Func<LocalDbContext> dbFactory,
        WebView2PdfExportHost pdfExportHost)
    {
        _imageStorage = imageStorage;
        _dbFactory = dbFactory;
        _pdfExportHost = pdfExportHost;
    }

    // ========================================================
    //  单篇笔记导出
    // ========================================================

    /// <summary>
    /// 导出笔记为 Markdown 文件夹（含 img/ 目录）
    /// </summary>
    /// <param name="note">要导出的笔记</param>
    /// <param name="parentDir">父目录（用户选择的保存路径）</param>
    /// <param name="appendTimestamp">
    /// 是否在顶层文件夹名追加时间戳；单篇导出为 true，
    /// 嵌套在已带时间戳的文件夹/全库根目录下时为 false
    /// </param>
    public async Task ExportNoteAsMarkdownAsync(LocalNote note, string parentDir, bool appendTimestamp = true)
    {
        var safeName = SanitizeFileName(note.Title);
        var dirName = appendTimestamp
            ? CreateTimestampedExportName(note.Title)
            : safeName;
        var noteDir = AllocateUniqueDirectory(Path.Combine(parentDir, dirName));
        Directory.CreateDirectory(noteDir);

        var imgDir = Path.Combine(noteDir, "img");
        var content = note.Content ?? string.Empty;

        // 处理图片引用：下载远程图片或复制本地图片
        content = await ProcessImageReferencesAsync(content, note.Id, imgDir);

        var mdPath = Path.Combine(noteDir, safeName + ".md");
        await File.WriteAllTextAsync(mdPath, content);
    }

    /// <summary>
    /// 导出笔记为 Word (.docx)
    /// </summary>
    public async Task ExportNoteAsWordAsync(LocalNote note, string filePath)
    {
        var markdown = await PrepareMarkdownWithEmbeddedImagesAsync(note.Content ?? string.Empty, note.Id);
        var html = MarkdownToHtml(markdown, note.Title);

        using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());

        // 使用 altChunk 嵌入 HTML（图片已转为 data URI）
        var chunkId = "altChunk1";
        var chunk = mainPart.AddAlternativeFormatImportPart(
            AlternativeFormatImportPartType.Html, chunkId);

        using (var stream = chunk.GetStream(FileMode.Create))
        using (var writer = new StreamWriter(stream))
        {
            await writer.WriteAsync(html);
        }

        var altChunk = new AltChunk { Id = chunkId };
        mainPart.Document.Body!.AppendChild(altChunk);
        mainPart.Document.Save();
    }

    /// <summary>
    /// 导出笔记为 PDF
    /// </summary>
    public async Task ExportNoteAsPdfAsync(LocalNote note, string filePath)
    {
        var targetDir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var markdown = await PrepareMarkdownWithEmbeddedImagesAsync(note.Content ?? string.Empty, note.Id);
        var html = WrapHtmlForPdf(MarkdownToHtml(markdown, note.Title));

        await _pdfExportHost.ExportHtmlToPdfAsync(html, filePath);
    }

    // ========================================================
    //  文件夹批量导出
    // ========================================================

    /// <summary>
    /// 导出文件夹为 Markdown（每篇笔记一个子文件夹）
    /// </summary>
    public async Task ExportFolderAsMarkdownAsync(LocalFolder folder, string parentDir)
    {
        var exportDir = AllocateUniqueDirectory(
            Path.Combine(parentDir, CreateTimestampedExportName(folder.Name)));
        Directory.CreateDirectory(exportDir);

        var notes = GetNotesForFolder(folder.Id);
        foreach (var note in notes)
        {
            // 根目录已带时间戳，内部笔记文件夹不再重复加戳
            await ExportNoteAsMarkdownAsync(note, exportDir, appendTimestamp: false);
        }

        // 递归导出子文件夹
        var subFolders = GetSubFolders(folder.Id);
        foreach (var sub in subFolders)
        {
            var subDir = AllocateUniqueDirectory(
                Path.Combine(exportDir, SanitizeFileName(sub.Name)));
            Directory.CreateDirectory(subDir);
            var subNotes = GetNotesForFolder(sub.Id);
            foreach (var note in subNotes)
            {
                await ExportNoteAsMarkdownAsync(note, subDir, appendTimestamp: false);
            }
        }
    }

    /// <summary>
    /// 导出文件夹为 Word（每篇笔记一个 .docx 文件）
    /// </summary>
    public async Task ExportFolderAsWordAsync(LocalFolder folder, string parentDir)
    {
        var exportDir = AllocateUniqueDirectory(
            Path.Combine(parentDir, CreateTimestampedExportName(folder.Name)));
        Directory.CreateDirectory(exportDir);

        var notes = GetNotesForFolder(folder.Id);
        foreach (var note in notes)
        {
            var filePath = AllocateUniqueFile(
                Path.Combine(exportDir, SanitizeFileName(note.Title) + ".docx"));
            await ExportNoteAsWordAsync(note, filePath);
        }

        // 递归导出子文件夹
        var subFolders = GetSubFolders(folder.Id);
        foreach (var sub in subFolders)
        {
            var subDir = AllocateUniqueDirectory(
                Path.Combine(exportDir, SanitizeFileName(sub.Name)));
            Directory.CreateDirectory(subDir);
            var subNotes = GetNotesForFolder(sub.Id);
            foreach (var note in subNotes)
            {
                var filePath = AllocateUniqueFile(
                    Path.Combine(subDir, SanitizeFileName(note.Title) + ".docx"));
                await ExportNoteAsWordAsync(note, filePath);
            }
        }
    }

    /// <summary>
    /// 导出文件夹为 PDF（每篇笔记一个 .pdf 文件）
    /// </summary>
    public async Task ExportFolderAsPdfAsync(LocalFolder folder, string parentDir)
    {
        var exportDir = AllocateUniqueDirectory(
            Path.Combine(parentDir, CreateTimestampedExportName(folder.Name)));
        Directory.CreateDirectory(exportDir);

        var notes = GetNotesForFolder(folder.Id);
        foreach (var note in notes)
        {
            var filePath = AllocateUniqueFile(
                Path.Combine(exportDir, SanitizeFileName(note.Title) + ".pdf"));
            await ExportNoteAsPdfAsync(note, filePath);
        }

        // 递归导出子文件夹
        var subFolders = GetSubFolders(folder.Id);
        foreach (var sub in subFolders)
        {
            var subDir = AllocateUniqueDirectory(
                Path.Combine(exportDir, SanitizeFileName(sub.Name)));
            Directory.CreateDirectory(subDir);
            var subNotes = GetNotesForFolder(sub.Id);
            foreach (var note in subNotes)
            {
                var filePath = AllocateUniqueFile(
                    Path.Combine(subDir, SanitizeFileName(note.Title) + ".pdf"));
                await ExportNoteAsPdfAsync(note, filePath);
            }
        }
    }

    // ========================================================
    //  全库归档导出（BiSheng Archive）
    // ========================================================

    /// <summary>
    /// 导出全部笔记、文件夹结构与图片为 BiSheng Archive（notes/ + images/ + manifest.json）
    /// </summary>
    public async Task<string> ExportFullLibraryAsync(string parentDir)
    {
        // 与单篇/文件夹导出统一使用本地时间戳格式 yyyyMMdd_HHmmss
        var archiveDir = AllocateUniqueDirectory(
            Path.Combine(parentDir, CreateTimestampedExportName("BiSheng-Archive")));
        var notesRoot = Path.Combine(archiveDir, "notes");
        var imagesRoot = Path.Combine(archiveDir, "images");
        Directory.CreateDirectory(notesRoot);
        Directory.CreateDirectory(imagesRoot);

        using var db = _dbFactory();
        var folders = db.Folders.Where(f => !f.IsDeleted).OrderBy(f => f.Name).ToList();
        var notes = db.Notes.Where(n => !n.IsDeleted).OrderBy(n => n.Title).ToList();
        var images = db.Images.ToList();
        var folderLookup = folders.ToDictionary(f => f.Id);

        foreach (var note in notes)
        {
            var folderPath = BuildFolderRelativePath(note.FolderId, folderLookup);
            var noteParent = string.IsNullOrEmpty(folderPath)
                ? notesRoot
                : Path.Combine(notesRoot, folderPath);
            Directory.CreateDirectory(noteParent);
            // 归档根目录已带时间戳，笔记子文件夹不再重复加戳
            await ExportNoteAsMarkdownAsync(note, noteParent, appendTimestamp: false);
        }

        var exportedImages = new List<object>();
        foreach (var image in images)
        {
            if (string.IsNullOrEmpty(image.FilePath) || !File.Exists(image.FilePath))
            {
                continue;
            }

            var ext = Path.GetExtension(image.FileName);
            if (string.IsNullOrEmpty(ext))
            {
                ext = Path.GetExtension(image.FilePath);
            }

            if (string.IsNullOrEmpty(ext))
            {
                ext = ".bin";
            }

            var destName = image.Id.ToString("N") + ext;
            var destPath = Path.Combine(imagesRoot, destName);
            File.Copy(image.FilePath, destPath, overwrite: true);
            exportedImages.Add(new
            {
                id = image.Id,
                noteId = image.NoteId,
                fileName = destName,
                contentType = image.ContentType
            });
        }

        var manifest = new
        {
            format = "bisheng-archive",
            version = 1,
            exportedAtUtc = DateTime.UtcNow,
            noteCount = notes.Count,
            folderCount = folders.Count,
            imageCount = exportedImages.Count,
            folders = folders.Select(f => new
            {
                id = f.Id,
                name = f.Name,
                parentId = f.ParentId,
                isFavorite = f.IsFavorite,
                isPinned = f.IsPinned,
                createdAt = f.CreatedAt,
                updatedAt = f.UpdatedAt
            }),
            notes = notes.Select(n => new
            {
                id = n.Id,
                title = n.Title,
                folderId = n.FolderId,
                folderPath = BuildFolderRelativePath(n.FolderId, folderLookup),
                isFavorite = n.IsFavorite,
                isPinned = n.IsPinned,
                createdAt = n.CreatedAt,
                updatedAt = n.UpdatedAt
            }),
            images = exportedImages
        };

        var manifestPath = Path.Combine(archiveDir, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        DataSafetySettings.Load().RecordFullExport();
        return archiveDir;
    }

    private static string BuildFolderRelativePath(Guid folderId, Dictionary<Guid, LocalFolder> lookup)
    {
        if (!lookup.TryGetValue(folderId, out var folder))
        {
            return "unknown";
        }

        var segments = new List<string>();
        var current = folder;
        var guard = 0;
        while (current != null && guard++ < 32)
        {
            segments.Insert(0, SanitizeFileName(current.Name));
            if (!current.ParentId.HasValue || !lookup.TryGetValue(current.ParentId.Value, out var parent))
            {
                break;
            }

            current = parent;
        }

        return Path.Combine(segments.ToArray());
    }

    // ========================================================
    //  内部辅助方法
    // ========================================================

    /// <summary>
    /// 处理 Markdown 内容中的图片引用：下载远程图片到 img/ 目录，替换为相对路径
    /// </summary>
    private async Task<string> ProcessImageReferencesAsync(string content, Guid noteId, string imgDir)
    {
        var localImages = _imageStorage.GetImagesForNote(noteId);
        var localImageMap = BuildImageUrlMap(localImages);
        var imgCounter = new ImageCounter();

        // 处理 ![alt](url) 格式
        content = await ReplaceAsync(MdImageRegex, content, async match =>
        {
            var alt = match.Groups[1].Value;
            var url = match.Groups[2].Value.Trim();
            var localPath = await SaveImageAsync(url, imgDir, localImageMap, imgCounter);
            return $"![{alt}]({localPath})";
        });

        // 处理 <img src="url"> 格式
        content = await ReplaceAsync(HtmlImgRegex, content, async match =>
        {
            var url = match.Groups[1].Value.Trim();
            var localPath = await SaveImageAsync(url, imgDir, localImageMap, imgCounter);
            return match.Value.Replace(url, localPath);
        });

        return content;
    }

    /// <summary>
    /// 为 Word/PDF 导出将图片嵌入为 data URI（支持 bisheng://img/{uuid}）
    /// </summary>
    private async Task<string> PrepareMarkdownWithEmbeddedImagesAsync(string content, Guid noteId)
    {
        var localImages = _imageStorage.GetImagesForNote(noteId);
        var urlMap = BuildImageUrlMap(localImages);

        content = await ReplaceAsync(MdImageRegex, content, async match =>
        {
            var alt = match.Groups[1].Value;
            var url = match.Groups[2].Value.Trim();
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return match.Value;
            }

            var filePath = await ResolveOrDownloadImageFileAsync(url, urlMap);
            if (filePath == null)
            {
                return match.Value;
            }

            return $"![{alt}]({ToDataUri(filePath)})";
        });

        content = await ReplaceAsync(HtmlImgRegex, content, async match =>
        {
            var url = match.Groups[1].Value.Trim();
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return match.Value;
            }

            var filePath = await ResolveOrDownloadImageFileAsync(url, urlMap);
            if (filePath == null)
            {
                return match.Value;
            }

            return match.Value.Replace(url, ToDataUri(filePath));
        });

        return content;
    }

    /// <summary>构建 URL → 本地文件路径映射（含 bisheng:// 与绝对路径）</summary>
    private static Dictionary<string, string> BuildImageUrlMap(IEnumerable<LocalImage> images)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var image in images)
        {
            var path = !string.IsNullOrEmpty(image.FilePath) && File.Exists(image.FilePath)
                ? image.FilePath
                : TryImagesDirectoryPath(image.Id.ToString());

            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            map[path] = path;
            if (!string.IsNullOrEmpty(image.FilePath))
            {
                map[image.FilePath] = path;
            }

            map[$"{BishengImagePrefix}{image.Id}"] = path;
            map[$"{BishengImagePrefix}{image.Id:N}"] = path;
            map[image.Id.ToString()] = path;
            map[image.Id.ToString("N")] = path;
        }

        return map;
    }

    /// <summary>解析图片 URL 对应的本地文件</summary>
    private static string? ResolveLocalImageFilePath(string url, IReadOnlyDictionary<string, string> urlMap)
    {
        if (urlMap.TryGetValue(url, out var mapped) && File.Exists(mapped))
        {
            return mapped;
        }

        if (TryParseBishengImageId(url, out var imageId))
        {
            if (urlMap.TryGetValue($"{BishengImagePrefix}{imageId}", out mapped) && File.Exists(mapped))
            {
                return mapped;
            }

            return TryImagesDirectoryPath(imageId);
        }

        if (Path.IsPathRooted(url) && File.Exists(url))
        {
            return url;
        }

        return null;
    }

    /// <summary>解析 bisheng://img/{uuid} 或下载 http(s) 图片到临时文件</summary>
    private async Task<string?> ResolveOrDownloadImageFileAsync(
        string url,
        IReadOnlyDictionary<string, string> urlMap)
    {
        var local = ResolveLocalImageFilePath(url, urlMap);
        if (local != null)
        {
            return local;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                var ext = Path.GetExtension(uri.AbsolutePath);
                if (string.IsNullOrEmpty(ext) || ext.Length > 6)
                {
                    ext = ".png";
                }

                var tempPath = Path.Combine(Path.GetTempPath(), $"bisheng-export-{Guid.NewGuid():N}{ext}");
                var bytes = await _httpClient.GetByteArrayAsync(uri);
                await File.WriteAllBytesAsync(tempPath, bytes);
                return tempPath;
            }
            catch (Exception ex)
            {
                LogHelper.Warn("导出时下载远程图片失败: {0} ({1})", url, ex.Message);
            }
        }

        return null;
    }

    /// <summary>images 目录下按 UUID 查找图片文件</summary>
    private static string? TryImagesDirectoryPath(string imageId)
    {
        var imagesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images");
        if (!Directory.Exists(imagesDir))
        {
            return null;
        }

        var pngPath = Path.Combine(imagesDir, $"{imageId}.png");
        if (File.Exists(pngPath))
        {
            return pngPath;
        }

        var matches = Directory.EnumerateFiles(imagesDir, $"{imageId}.*").ToList();
        return matches.Count > 0 ? matches[0] : null;
    }

    private static bool TryParseBishengImageId(string url, out string imageId)
    {
        imageId = string.Empty;
        if (!url.StartsWith(BishengImagePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        imageId = url[BishengImagePrefix.Length..].Trim();
        return !string.IsNullOrWhiteSpace(imageId);
    }

    /// <summary>本地图片文件转 data URI</summary>
    private static string ToDataUri(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var mime = GuessContentType(filePath);
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }

    private static string GuessContentType(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            _ => "application/octet-stream",
        };
    }

    /// <summary>
    /// 异步正则替换辅助方法
    /// </summary>
    private static async Task<string> ReplaceAsync(Regex regex, string input, Func<Match, Task<string>> evaluator)
    {
        var matches = regex.Matches(input);
        if (matches.Count == 0) return input;

        var result = new System.Text.StringBuilder();
        var lastIndex = 0;

        foreach (Match match in matches)
        {
            result.Append(input, lastIndex, match.Index - lastIndex);
            var replacement = await evaluator(match);
            result.Append(replacement);
            lastIndex = match.Index + match.Length;
        }

        result.Append(input, lastIndex, input.Length - lastIndex);
        return result.ToString();
    }

    /// <summary>
    /// 图片计数器（用于异步方法中避免 ref 参数）
    /// </summary>
    private class ImageCounter
    {
        public int Value { get; set; }
    }

    /// <summary>
    /// 保存图片到 img/ 目录并返回相对路径
    /// </summary>
    private async Task<string> SaveImageAsync(
        string url, string imgDir,
        Dictionary<string, string> localImageMap, ImageCounter counter)
    {
        Directory.CreateDirectory(imgDir);

        var sourcePath = ResolveLocalImageFilePath(url, localImageMap);
        if (sourcePath != null)
        {
            var ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrEmpty(ext))
            {
                ext = ".png";
            }

            var fileName = $"image_{counter.Value++}{ext}";
            var destPath = Path.Combine(imgDir, fileName);
            File.Copy(sourcePath, destPath, overwrite: true);
            return $"./img/{fileName}";
        }

        // 尝试下载远程图片
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                var ext = Path.GetExtension(uri.AbsolutePath);
                if (string.IsNullOrEmpty(ext) || ext.Length > 6)
                    ext = ".png";

                var fileName = $"image_{counter.Value++}{ext}";
                var destPath = Path.Combine(imgDir, fileName);

                var bytes = await _httpClient.GetByteArrayAsync(uri);
                await File.WriteAllBytesAsync(destPath, bytes);
                return $"./img/{fileName}";
            }
            catch
            {
                // 下载失败，保留原始 URL
                return url;
            }
        }

        // 无法处理的图片，保留原始引用
        return url;
    }

    /// <summary>
    /// 将 Markdown 内容转换为完整 HTML 文档
    /// </summary>
    private static string MarkdownToHtml(string markdown, string title)
    {
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        var bodyHtml = Markdig.Markdown.ToHtml(markdown, pipeline);

        return $@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<title>{System.Net.WebUtility.HtmlEncode(title)}</title>
<style>
body {{ font-family: 'Segoe UI', 'Microsoft YaHei', sans-serif; line-height: 1.8; color: #333; max-width: 800px; margin: 0 auto; padding: 20px; }}
h1 {{ font-size: 2em; border-bottom: 2px solid #e0e0e0; padding-bottom: 0.3em; }}
h2 {{ font-size: 1.5em; border-bottom: 1px solid #e0e0e0; padding-bottom: 0.2em; }}
h3 {{ font-size: 1.25em; }}
code {{ background: #f5f5f5; padding: 2px 6px; border-radius: 3px; font-size: 0.9em; }}
pre {{ background: #f5f5f5; padding: 16px; border-radius: 6px; overflow-x: auto; }}
pre code {{ background: none; padding: 0; }}
blockquote {{ border-left: 4px solid #ddd; margin: 0; padding: 0 16px; color: #666; }}
table {{ border-collapse: collapse; width: 100%; }}
th, td {{ border: 1px solid #ddd; padding: 8px 12px; text-align: left; }}
th {{ background: #f5f5f5; font-weight: 600; }}
img {{ max-width: 100%; height: auto; }}
</style>
</head>
<body>
{bodyHtml}
</body>
</html>";
    }

    /// <summary>
    /// 为 PDF 导出包装 HTML（添加分页等打印样式）
    /// </summary>
    private static string WrapHtmlForPdf(string html)
    {
        // 在已有样式基础上追加打印优化
        return html.Replace(
            "</style>",
            @"
@media print {
    body { max-width: none; }
    h1, h2, h3 { page-break-after: avoid; }
    pre, blockquote { page-break-inside: avoid; }
    img { page-break-inside: avoid; }
}
</style>");
    }

    /// <summary>获取指定文件夹下的所有笔记</summary>
    private List<LocalNote> GetNotesForFolder(Guid folderId)
    {
        using var db = _dbFactory();
        return db.Notes
            .Where(n => n.FolderId == folderId && !n.IsDeleted)
            .OrderBy(n => n.Title)
            .ToList();
    }

    /// <summary>获取指定文件夹的子文件夹</summary>
    private List<LocalFolder> GetSubFolders(Guid parentId)
    {
        using var db = _dbFactory();
        return db.Folders
            .Where(f => f.ParentId == parentId && !f.IsDeleted)
            .OrderBy(f => f.Name)
            .ToList();
    }

    /// <summary>
    /// 生成带本地时间戳的导出名（不含扩展名），供对话框默认文件名等使用
    /// </summary>
    /// <param name="baseName">原始标题或文件夹名</param>
    /// <returns>{清理后名称}_{yyyyMMdd_HHmmss}</returns>
    public static string CreateTimestampedExportName(string baseName)
    {
        return $"{SanitizeFileName(baseName)}_{CreateExportTimestamp()}";
    }

    /// <summary>生成导出用本地时间戳字符串</summary>
    private static string CreateExportTimestamp()
    {
        return DateTime.Now.ToString("yyyyMMdd_HHmmss");
    }

    /// <summary>
    /// 分配不冲突的目录路径；已存在时追加 _2、_3…
    /// </summary>
    /// <param name="path">期望目录路径</param>
    private static string AllocateUniqueDirectory(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return path;
        }

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{path}_{i}";
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }

        return $"{path}_{Guid.NewGuid():N}";
    }

    /// <summary>
    /// 分配不冲突的文件路径；已存在时在扩展名前追加 _2、_3…
    /// </summary>
    /// <param name="path">期望文件路径</param>
    private static string AllocateUniqueFile(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(dir, $"{name}_{Guid.NewGuid():N}{ext}");
    }

    /// <summary>清理文件名中的非法字符</summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "untitled" : sanitized;
    }
}

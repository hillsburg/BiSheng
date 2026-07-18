using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;

namespace BiSheng.Latte.Services.Search;

/// <summary>基于本地 SQLite 的笔记全文搜索（M1：内存扫描 + PlainText 提取）</summary>
public sealed class NoteContentSearchService : INoteContentSearchService
{
    private readonly Func<LocalDbContext> _dbFactory;

    /// <summary>构造搜索服务</summary>
    public NoteContentSearchService(Func<LocalDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<NoteSearchResultItem>> SearchAsync(
        string query,
        bool titleOnly,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = query?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
            {
                return (IReadOnlyList<NoteSearchResultItem>)Array.Empty<NoteSearchResultItem>();
            }

            using var db = _dbFactory();
            var folders = db.Folders.Where(f => !f.IsDeleted).ToDictionary(f => f.Id);
            var notes = db.Notes.Where(n => !n.IsDeleted).ToList();
            var results = new List<NoteSearchResultItem>();

            foreach (var note in notes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hitCount = CountHits(note, normalized, titleOnly);
                if (hitCount <= 0)
                {
                    continue;
                }

                results.Add(new NoteSearchResultItem
                {
                    NoteId = note.Id,
                    FolderId = note.FolderId,
                    Title = note.Title,
                    FolderPath = BuildFolderPath(note.FolderId, folders),
                    HitCount = hitCount
                });
            }

            return results
                .OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<NoteSearchHitItem>> GetHitsAsync(
        Guid noteId,
        string query,
        bool titleOnly,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = query?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
            {
                return (IReadOnlyList<NoteSearchHitItem>)Array.Empty<NoteSearchHitItem>();
            }

            using var db = _dbFactory();
            var note = db.Notes.Find(noteId);
            if (note == null || note.IsDeleted)
            {
                return (IReadOnlyList<NoteSearchHitItem>)Array.Empty<NoteSearchHitItem>();
            }

            return (IReadOnlyList<NoteSearchHitItem>)BuildHitItems(note, normalized, titleOnly);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> GetNoteContentAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var db = _dbFactory();
            return db.Notes.Find(noteId)?.Content;
        }, cancellationToken);
    }

    private static int CountHits(LocalNote note, string query, bool titleOnly)
    {
        var count = 0;
        if (note.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
        {
            count++;
        }

        if (titleOnly)
        {
            return count;
        }

        var plain = NoteSearchTextExtractor.Extract(note.Content).PlainText;
        count += NoteSearchTextExtractor.FindAll(plain, query).Count;
        return count;
    }

    private static List<NoteSearchHitItem> BuildHitItems(LocalNote note, string query, bool titleOnly)
    {
        var items = new List<NoteSearchHitItem>();
        var index = 0;
        var bodyOccurrence = 0;

        if (note.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
        {
            var titleOffset = note.Title.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
            var titleMarkdownPos = NoteSearchNavigation.FindQueryInMarkdown(note.Content ?? string.Empty, query, bodyOccurrence);
            items.Add(new NoteSearchHitItem
            {
                Index = index++,
                PlainTextOffset = titleOffset,
                Length = query.Length,
                Snippet = NoteSearchTextExtractor.BuildSnippet(note.Title, titleOffset, query.Length),
                IsTitleHit = true,
                MarkdownCaretOffset = titleMarkdownPos.CaretOffset,
                MarkdownSelectionLength = titleMarkdownPos.SelectionLength
            });
        }

        if (!titleOnly)
        {
            var extraction = NoteSearchTextExtractor.Extract(note.Content);
            foreach (var (offset, length) in NoteSearchTextExtractor.FindAll(extraction.PlainText, query))
            {
                var markdownPos = NoteSearchNavigation.MapFromExtraction(extraction, offset, length);
                if (markdownPos.SelectionLength <= 0)
                {
                    markdownPos = NoteSearchNavigation.FindQueryInMarkdown(
                        note.Content ?? string.Empty,
                        query,
                        bodyOccurrence);
                }

                items.Add(new NoteSearchHitItem
                {
                    Index = index++,
                    PlainTextOffset = offset,
                    Length = length,
                    Snippet = NoteSearchTextExtractor.BuildSnippet(extraction.PlainText, offset, length),
                    MarkdownCaretOffset = markdownPos.CaretOffset,
                    MarkdownSelectionLength = markdownPos.SelectionLength
                });
                bodyOccurrence++;
            }
        }

        return items;
    }

    private static string BuildFolderPath(Guid folderId, IReadOnlyDictionary<Guid, LocalFolder> folders)
    {
        var parts = new List<string>();
        var currentId = folderId;
        var guard = 0;

        while (folders.TryGetValue(currentId, out var folder) && guard++ < 32)
        {
            parts.Add(folder.Name);
            if (folder.ParentId == null)
            {
                break;
            }

            currentId = folder.ParentId.Value;
        }

        parts.Reverse();
        return string.Join(" / ", parts);
    }
}

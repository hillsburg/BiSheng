using BiSheng.Latte.Models;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace BiSheng.Latte.Services.Diff;

/// <summary>冲突正文行级 Diff（基于 DiffPlex）</summary>
public static class ConflictTextDiffer
{
    /// <summary>
    /// 以本地为旧、远端为新，生成统一 Diff 行列表。
    /// Deleted = 仅本地有；Inserted = 仅远端有。
    /// </summary>
    public static IReadOnlyList<ConflictDiffLine> BuildUnified(string? localContent, string? remoteContent)
    {
        var diff = InlineDiffBuilder.Diff(localContent ?? string.Empty, remoteContent ?? string.Empty);
        var lines = new List<ConflictDiffLine>(diff.Lines.Count);

        foreach (var line in diff.Lines)
        {
            switch (line.Type)
            {
                case ChangeType.Deleted:
                    lines.Add(new ConflictDiffLine(ConflictDiffKind.Deleted, "-", line.Text));
                    break;
                case ChangeType.Inserted:
                    lines.Add(new ConflictDiffLine(ConflictDiffKind.Inserted, "+", line.Text));
                    break;
                case ChangeType.Modified:
                    // InlineDiffBuilder 通常拆成 Deleted+Inserted；若出现 Modified 按插入展示
                    lines.Add(new ConflictDiffLine(ConflictDiffKind.Inserted, "+", line.Text));
                    break;
                case ChangeType.Imaginary:
                    break;
                default:
                    lines.Add(new ConflictDiffLine(ConflictDiffKind.Equal, " ", line.Text));
                    break;
            }
        }

        return lines;
    }
}

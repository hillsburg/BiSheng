using BiSheng.Latte.Models;
using BiSheng.Latte.Services.Diff;

namespace BiSheng.Latte.Tests.Services;

public class ConflictTextDifferTests
{
    [Fact]
    public void BuildUnified_MarksInsertedAndDeletedLines()
    {
        var lines = ConflictTextDiffer.BuildUnified("a\nb\nc", "a\nx\nc");

        Assert.Contains(lines, l => l.Kind == ConflictDiffKind.Deleted && l.Text == "b");
        Assert.Contains(lines, l => l.Kind == ConflictDiffKind.Inserted && l.Text == "x");
        Assert.Contains(lines, l => l.Kind == ConflictDiffKind.Equal && l.Text == "a");
        Assert.Contains(lines, l => l.Kind == ConflictDiffKind.Equal && l.Text == "c");
    }

    [Fact]
    public void BuildUnified_TreatsNullAsEmpty()
    {
        var lines = ConflictTextDiffer.BuildUnified(null, "only");
        Assert.Contains(lines, l => l.Kind == ConflictDiffKind.Inserted && l.Text == "only");
    }
}

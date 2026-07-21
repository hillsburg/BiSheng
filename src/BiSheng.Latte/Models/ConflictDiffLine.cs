namespace BiSheng.Latte.Models;

/// <summary>冲突 Diff 行类型</summary>
public enum ConflictDiffKind
{
    /// <summary>两侧相同</summary>
    Equal,

    /// <summary>仅本地有（相对远端为删除）</summary>
    Deleted,

    /// <summary>仅远端有（相对本地为新增）</summary>
    Inserted,
}

/// <summary>统一 Diff 视图的一行</summary>
public sealed class ConflictDiffLine
{
    public ConflictDiffLine(ConflictDiffKind kind, string marker, string text)
    {
        Kind = kind;
        Marker = marker;
        Text = text;
    }

    /// <summary>变更类型</summary>
    public ConflictDiffKind Kind { get; }

    /// <summary>行首标记（空格 / - / +）</summary>
    public string Marker { get; }

    /// <summary>行文本（不含换行）</summary>
    public string Text { get; }

    /// <summary>展示用完整行</summary>
    public string DisplayText => Marker == " " ? $"  {Text}" : $"{Marker} {Text}";
}

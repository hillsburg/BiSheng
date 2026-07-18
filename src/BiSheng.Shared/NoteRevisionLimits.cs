namespace BiSheng.Shared;

/// <summary>每篇笔记可保留的历史版本上限（服务端与本地均为 FIFO 裁剪）</summary>
public static class NoteRevisionLimits
{
    /// <summary>单篇笔记最多保留的历史条数</summary>
    public const int MaxPerNote = 100;
}

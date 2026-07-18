using BiSheng.Server.Services.Images;

namespace BiSheng.Server.Tests.Images;

/// <summary>PR5：笔记正文图片引用扫描</summary>
public class NoteImageReferenceScannerTests
{
    /// <summary>识别 bisheng:// 与 /api/images/ 两种引用</summary>
    [Fact]
    public void ExtractImageIds_ParsesBothSchemes()
    {
        var id1 = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var id2 = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var content =
            $"![a](bisheng://img/{id1})\n" +
            $"![b](/api/images/{id2})";

        var ids = NoteImageReferenceScanner.ExtractImageIds(content);
        Assert.Equal(2, ids.Count);
        Assert.Contains(id1, ids);
        Assert.Contains(id2, ids);
    }

    /// <summary>空正文返回空集合</summary>
    [Fact]
    public void ExtractImageIds_Empty_ReturnsEmpty()
    {
        Assert.Empty(NoteImageReferenceScanner.ExtractImageIds((string?)null));
        Assert.Empty(NoteImageReferenceScanner.ExtractImageIds(""));
    }
}

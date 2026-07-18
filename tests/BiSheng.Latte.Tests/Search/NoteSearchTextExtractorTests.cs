using BiSheng.Latte.Services.Search;
using Xunit;

namespace BiSheng.Latte.Tests.Search;

/// <summary>NoteSearchTextExtractor：Markdown → PlainText</summary>
public class NoteSearchTextExtractorTests
{
    /// <summary>标题标记应被剥离</summary>
    [Fact]
    public void Extract_Heading_StripsHashMarkers()
    {
        var result = NoteSearchTextExtractor.Extract("# 项目周报\n\n正文");

        Assert.Contains("项目周报", result.PlainText);
        Assert.Contains("正文", result.PlainText);
        Assert.DoesNotContain("#", result.PlainText);
    }

    /// <summary>链接保留可见文案</summary>
    [Fact]
    public void Extract_Link_KeepsLinkText()
    {
        var result = NoteSearchTextExtractor.Extract("详见 [设计文档](https://example.com) 说明");

        Assert.Contains("设计文档", result.PlainText);
        Assert.DoesNotContain("https", result.PlainText);
    }

    /// <summary>FindAll 忽略大小写</summary>
    [Fact]
    public void FindAll_IsCaseInsensitive()
    {
        var hits = NoteSearchTextExtractor.FindAll("Hello WORLD", "world");

        Assert.Single(hits);
        Assert.Equal(6, hits[0].Offset);
    }
}

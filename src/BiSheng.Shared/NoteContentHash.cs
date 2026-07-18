using System;
using System.Security.Cryptography;
using System.Text;

namespace BiSheng.Shared;

/// <summary>
/// 笔记标题+正文的内容指纹，用于历史版本去重。
/// 格式：SHA-256 十六进制大写，输入为 <c>title\0content</c>。
/// </summary>
public static class NoteContentHash
{
    /// <summary>计算标题与正文的组合哈希</summary>
    /// <param name="title">笔记标题</param>
    /// <param name="content">Markdown 正文，可为 null</param>
    public static string Compute(string title, string? content)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes($"{title}\0{content ?? string.Empty}"));
        return BitConverter.ToString(bytes).Replace("-", string.Empty);
    }
}

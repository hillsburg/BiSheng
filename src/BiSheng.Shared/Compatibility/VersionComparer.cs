using System;
using System.Collections.Generic;

namespace BiSheng.Shared.Compatibility;

/// <summary>语义化版本比较（忽略可选的 v 前缀与预发布后缀）</summary>
public static class VersionComparer
{
    /// <summary>去掉 tag 前缀 v/V</summary>
    public static string Normalize(string tagOrVersion)
    {
        if (string.IsNullOrWhiteSpace(tagOrVersion))
        {
            return "0.0.0";
        }

        var s = tagOrVersion.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase) && s.Length > 1)
        {
            s = s.Substring(1);
        }

        return s;
    }

    /// <summary>
    /// 比较版本。返回 &gt;0 表示 left 更新，0 相等，&lt;0 表示 left 更旧。
    /// </summary>
    public static int Compare(string left, string right)
    {
        if (Version.TryParse(Pad(left), out var a)
            && Version.TryParse(Pad(right), out var b))
        {
            return a.CompareTo(b);
        }

        return string.Compare(
            Normalize(left),
            Normalize(right),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>left 是否满足不低于 right 的门槛</summary>
    public static bool IsAtLeast(string actual, string minimum) =>
        Compare(actual, minimum) >= 0;

    private static string Pad(string version)
    {
        var parts = Normalize(version).Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        // netstandard2.0：不用集合表达式 / range
        var list = new List<string>(parts);
        while (list.Count < 3)
        {
            list.Add("0");
        }

        for (var i = 0; i < list.Count && i < 4; i++)
        {
            var dash = list[i].IndexOf('-');
            if (dash > 0)
            {
                list[i] = list[i].Substring(0, dash);
            }
        }

        if (list.Count > 4)
        {
            list.RemoveRange(4, list.Count - 4);
        }

        return string.Join(".", list);
    }
}

using System.Reflection;

namespace BiSheng.Latte.Services;

/// <summary>Latte 客户端版本字符串（来自程序集 InformationalVersion / Version）</summary>
public static class LatteAppVersion
{
    /// <summary>界面与备份元数据展示用版本号</summary>
    public static string DisplayVersion
    {
        get
        {
            var asm = Assembly.GetExecutingAssembly();
            var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                var plus = informational.IndexOf('+');
                return plus >= 0 ? informational[..plus] : informational;
            }

            var version = asm.GetName().Version;
            return version == null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }
}

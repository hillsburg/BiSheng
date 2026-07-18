namespace BiSheng.Server.Data;

/// <summary>服务端 SQLite 默认路径（相对路径会落在进程工作目录，而非 bin）</summary>
public static class ServerDatabasePaths
{
    /// <summary>默认库文件名</summary>
    public const string DatabaseFileName = "bisheng.db";

    /// <summary>
    /// 运行时默认库路径：与主程序 DLL 同目录（Debug 时为 bin/Debug/net8.0/bisheng.db）
    /// </summary>
    public static string DefaultDatabaseFile =>
        Path.Combine(AppContext.BaseDirectory, DatabaseFileName);

    /// <summary>未配置 ConnectionStrings 时使用的连接串（Shared Cache + 忙等待，减轻并发 Push 锁冲突）</summary>
    public static string DefaultConnectionString =>
        $"Data Source={DefaultDatabaseFile};Cache=Shared;Default Timeout=30";
}

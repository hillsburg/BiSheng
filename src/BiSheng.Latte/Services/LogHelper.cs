using System.IO;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace BiSheng.Latte.Services;

/// <summary>
/// 日志工具类：基于 NLog 的静态日志封装
/// 
/// 日志输出到 LocalAppData 下的 log 文件夹：
/// - log/app-{date}.log   —— 所有级别（Trace ~ Fatal）
/// - log/error-{date}.log —— 仅 Error 和 Fatal
/// 
/// 使用方式：
/// LogHelper.Info("同步完成");
/// LogHelper.Error("上传失败", ex);
/// </summary>
public static class LogHelper
{
    private static readonly Logger Logger = LogManager.GetLogger("BiSheng.Latte");
    private static bool _initialized;

    /// <summary>
    /// 初始化 NLog 配置（在应用启动时调用一次）
    /// 日志目录：%LocalAppData%\BiSheng\Latte\log\
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        LatteAppPaths.EnsureRoot();
        var logDir = LatteAppPaths.LogDirectory;
        if (!Directory.Exists(logDir))
            Directory.CreateDirectory(logDir);

        var config = new LoggingConfiguration();

        // 通用日志文件：所有级别
        var allFileTarget = new FileTarget("allFile")
        {
            FileName = Path.Combine(logDir, "app-${shortdate}.log"),
            Layout = "${longdate} | ${level:uppercase=true:padding=-5} | ${message} ${exception:format=tostring}",
            Encoding = System.Text.Encoding.UTF8,
            ArchiveEvery = FileArchivePeriod.Day,
            MaxArchiveFiles = 30
        };

        // 错误日志文件：仅 Error + Fatal
        var errorFileTarget = new FileTarget("errorFile")
        {
            FileName = Path.Combine(logDir, "error-${shortdate}.log"),
            Layout = "${longdate} | ${level:uppercase=true:padding=-5} | ${message} ${exception:format=tostring}",
            Encoding = System.Text.Encoding.UTF8,
            ArchiveEvery = FileArchivePeriod.Day,
            MaxArchiveFiles = 30
        };

        config.AddTarget(allFileTarget);
        config.AddTarget(errorFileTarget);

        // 所有级别 → 通用日志
        config.LoggingRules.Add(new LoggingRule("*", LogLevel.Trace, allFileTarget));

        // Error 及以上 → 错误日志
        config.LoggingRules.Add(new LoggingRule("*", LogLevel.Error, errorFileTarget));

        LogManager.Configuration = config;
    }

    /// <summary>
    /// 关闭 NLog，刷新缓冲区（在应用退出时调用）
    /// </summary>
    public static void Shutdown()
    {
        LogManager.Shutdown();
    }

    // ===== 日志方法 =====

    public static void Trace(string message) => Logger.Trace(message);

    public static void Trace(string format, params object[] args) => Logger.Trace(format, args);

    public static void Debug(string message) => Logger.Debug(message);

    public static void Info(string message) => Logger.Info(message);

    public static void Warn(string message) => Logger.Warn(message);

    public static void Error(string message, Exception? exception = null)
    {
        if (exception != null)
            Logger.Error(exception, message);
        else
            Logger.Error(message);
    }

    public static void Fatal(string message, Exception? exception = null)
    {
        if (exception != null)
            Logger.Fatal(exception, message);
        else
            Logger.Fatal(message);
    }

    /// <summary>
    /// 带格式化参数的日志（如 LogHelper.Info("同步完成，共 {0} 条", count)）
    /// </summary>
    public static void Info(string format, params object[] args) => Logger.Info(format, args);

    public static void Warn(string format, params object[] args) => Logger.Warn(format, args);

    public static void Debug(string format, params object[] args) => Logger.Debug(format, args);
}

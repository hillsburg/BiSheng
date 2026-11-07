using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace BiSheng.Latte.Services;

/// <summary>
/// 全局未处理异常兜底：保证任何漏网异常至少落一条日志，并给用户可理解的提示。
/// - UI 线程异常：记录并提示后标记已处理，让进程继续运行（自动保存仍在工作，避免直接丢失编辑）；
/// - 未观察的 Task 异常：仅记录并标记已观察（.NET 默认不会因此终止进程，但会静默吞掉）；
/// - 非 UI 线程致命异常：无法阻止进程终止，只尽量把日志刷到磁盘。
/// </summary>
public static class GlobalExceptionHandler
{
    /// <summary>
    /// 短时间内连续发生的 UI 异常只弹一次窗：异常常常在每次布局/渲染时重复抛出，
    /// 每次都弹模态框会让用户无法操作。
    /// </summary>
    private static readonly TimeSpan DialogCooldown = TimeSpan.FromSeconds(10);

    private static int _registered;
    private static int _dialogShowing;
    private static DateTime _lastDialogUtc = DateTime.MinValue;

    /// <summary>注册全部处理器（重复调用无副作用）</summary>
    public static void Register(Application app)
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0)
        {
            return;
        }

        app.DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogHelper.Error("UI 线程未处理异常", e.Exception);
        e.Handled = true;

        // 正在弹窗（弹窗本身触发异常）或冷却期内：只记日志
        if (Interlocked.CompareExchange(ref _dialogShowing, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            if (now - _lastDialogUtc < DialogCooldown)
            {
                return;
            }

            _lastDialogUtc = now;
            ShowErrorDialog(
                "Latte 遇到了一个未预期的错误，已记录到日志。\n\n" +
                $"{e.Exception.Message}\n\n" +
                "笔记会继续自动保存。如果界面表现异常，建议重启 Latte。",
                "意外错误");
        }
        finally
        {
            Interlocked.Exchange(ref _dialogShowing, 0);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogHelper.Error("后台任务未观察异常", e.Exception);
        e.SetObserved();
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogHelper.Fatal(
            e.IsTerminating ? "致命未处理异常，进程即将终止" : "未处理异常",
            e.ExceptionObject as Exception);

        if (e.IsTerminating)
        {
            // 进程马上结束，NLog 缓冲区不刷就会丢掉这条最关键的日志
            LogHelper.Shutdown();
        }
    }

    /// <summary>
    /// 启动阶段致命错误提示：此时主题资源或主窗口可能尚未就绪，
    /// 自定义弹窗失败时回退到系统 MessageBox，保证用户一定能看到原因。
    /// </summary>
    public static void ShowStartupFailure(Exception exception)
    {
        ShowErrorDialog(
            "Latte 启动失败，已记录到日志。\n\n" +
            $"{exception.Message}\n\n" +
            $"日志目录：{LatteAppPaths.LogDirectory}",
            "启动失败");
    }

    private static void ShowErrorDialog(string message, string title)
    {
        try
        {
            AppDialog.Error(message, title);
        }
        catch (Exception dialogError)
        {
            LogHelper.Warn("自定义错误弹窗失败，回退系统弹窗: {0}", dialogError.Message);
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

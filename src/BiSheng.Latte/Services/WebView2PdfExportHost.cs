using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BiSheng.Latte.Services;

/// <summary>
/// WebView2 PDF 导出宿主：在隐藏窗口中加载 HTML 并调用 PrintToPdf。
/// 单例复用 WebView2 实例，导出操作串行执行。
/// </summary>
public sealed class WebView2PdfExportHost : IDisposable
{
    /// <summary>NavigateToString 安全长度上限，超出则写临时文件</summary>
    private const int NavigateToStringMaxLength = 1_500_000;

    /// <summary>导航完成等待超时</summary>
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromMinutes(2);

    /// <summary>A4 纸宽（英寸）</summary>
    private const double A4WidthInches = 8.27;

    /// <summary>A4 纸高（英寸）</summary>
    private const double A4HeightInches = 11.69;

    /// <summary>页边距（英寸，对应约 20mm / 15mm）</summary>
    private const double MarginVerticalInches = 20.0 / 25.4;

    private const double MarginHorizontalInches = 15.0 / 25.4;

    private readonly SemaphoreSlim _exportGate = new(1, 1);
    private readonly string _userDataFolder;

    private Window? _hostWindow;
    private WebView2? _webView;
    private CoreWebView2Environment? _environment;
    private bool _initialized;
    private int _disposeState;

    /// <summary>初始化 WebView2 用户数据目录</summary>
    public WebView2PdfExportHost()
    {
        _userDataFolder = Path.Combine(Path.GetTempPath(), "BiSheng", "WebView2Pdf");
    }

    /// <summary>将 HTML 渲染为 PDF 并写入目标路径</summary>
    /// <param name="html">完整 HTML 文档</param>
    /// <param name="filePath">PDF 输出绝对或相对路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task ExportHtmlToPdfAsync(
        string html,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(html))
        {
            throw new ArgumentException("HTML 内容为空。", nameof(html));
        }

        var absolutePath = Path.GetFullPath(filePath);
        var targetDir = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        await _exportGate.WaitAsync(cancellationToken);
        try
        {
            await RunOnUiThreadAsync(
                () => ExportOnUiThreadAsync(html, absolutePath, cancellationToken),
                cancellationToken);
        }
        finally
        {
            _exportGate.Release();
        }
    }

    /// <summary>在 UI 线程上完成 HTML 加载与 PDF 打印</summary>
    private async Task ExportOnUiThreadAsync(string html, string absolutePath, CancellationToken cancellationToken)
    {
        string? tempHtmlPath = null;
        string? tempHtmlDir = null;

        try
        {
            await EnsureWebViewAsync(cancellationToken);

            if (html.Length <= NavigateToStringMaxLength)
            {
                await LoadHtmlFromStringAsync(html, cancellationToken);
            }
            else
            {
                tempHtmlDir = Path.Combine(Path.GetTempPath(), "BiSheng", "pdf-export", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempHtmlDir);
                tempHtmlPath = Path.Combine(tempHtmlDir, "index.html");
                await File.WriteAllTextAsync(tempHtmlPath, html, Encoding.UTF8, cancellationToken);
                await LoadHtmlFromFileAsync(tempHtmlPath, cancellationToken);
            }

            await PrintToPdfAsync(absolutePath, cancellationToken);
            LogHelper.Info("PDF 导出成功: {0}", absolutePath);
        }
        catch (Exception ex) when (ex is not InvalidOperationException and not OperationCanceledException)
        {
            LogHelper.Error("PDF 导出失败", ex);
            throw new InvalidOperationException($"PDF 导出失败: {ex.Message}", ex);
        }
        finally
        {
            TryDeleteTempHtml(tempHtmlPath, tempHtmlDir);
        }
    }

    /// <summary>确保 WebView2 运行时已安装并完成控件初始化</summary>
    private async Task EnsureWebViewAsync(CancellationToken cancellationToken)
    {
        if (_initialized && _webView?.CoreWebView2 != null)
        {
            return;
        }

        var runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
        if (string.IsNullOrWhiteSpace(runtimeVersion))
        {
            throw new InvalidOperationException(
                "未检测到 WebView2 Runtime。请安装 Microsoft Edge WebView2 运行时后重试。");
        }

        Directory.CreateDirectory(_userDataFolder);

        _hostWindow = new Window
        {
            Width = 1,
            Height = 1,
            Left = -10000,
            Top = -10000,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Visibility = Visibility.Hidden,
        };

        _webView = new WebView2();
        _hostWindow.Content = _webView;
        _hostWindow.Show();

        _environment = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
        cancellationToken.ThrowIfCancellationRequested();
        await _webView.EnsureCoreWebView2Async(_environment);

        _initialized = true;
        LogHelper.Info("WebView2 PDF 导出宿主已初始化，Runtime 版本: {0}", runtimeVersion);
    }

    /// <summary>通过 NavigateToString 加载 HTML</summary>
    private Task LoadHtmlFromStringAsync(string html, CancellationToken cancellationToken)
    {
        if (_webView?.CoreWebView2 == null)
        {
            throw new InvalidOperationException("WebView2 尚未初始化。");
        }

        var navigationTask = WaitForNavigationAsync(cancellationToken);
        _webView.CoreWebView2.NavigateToString(html);
        return navigationTask;
    }

    /// <summary>通过本地文件 URI 加载 HTML（适用于大文档）</summary>
    private Task LoadHtmlFromFileAsync(string htmlPath, CancellationToken cancellationToken)
    {
        if (_webView?.CoreWebView2 == null)
        {
            throw new InvalidOperationException("WebView2 尚未初始化。");
        }

        var navigationTask = WaitForNavigationAsync(cancellationToken);
        _webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
        return navigationTask;
    }

    /// <summary>等待当前导航完成</summary>
    private Task WaitForNavigationAsync(CancellationToken cancellationToken)
    {
        if (_webView?.CoreWebView2 == null)
        {
            throw new InvalidOperationException("WebView2 尚未初始化。");
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _webView.CoreWebView2!.NavigationCompleted -= OnNavigationCompleted;
            if (e.IsSuccess)
            {
                tcs.TrySetResult();
            }
            else
            {
                tcs.TrySetException(new InvalidOperationException($"HTML 加载失败: {e.WebErrorStatus}"));
            }
        }

        _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        return tcs.Task.WaitAsync(NavigationTimeout, cancellationToken);
    }

    /// <summary>调用 WebView2 PrintToPdf 写入文件</summary>
    private async Task PrintToPdfAsync(string absolutePath, CancellationToken cancellationToken)
    {
        if (_webView?.CoreWebView2 == null || _environment == null)
        {
            throw new InvalidOperationException("WebView2 尚未初始化。");
        }

        var printSettings = _environment.CreatePrintSettings();
        printSettings.PageWidth = A4WidthInches;
        printSettings.PageHeight = A4HeightInches;
        printSettings.MarginTop = MarginVerticalInches;
        printSettings.MarginBottom = MarginVerticalInches;
        printSettings.MarginLeft = MarginHorizontalInches;
        printSettings.MarginRight = MarginHorizontalInches;
        printSettings.ShouldPrintBackgrounds = true;
        printSettings.ShouldPrintHeaderAndFooter = false;

        cancellationToken.ThrowIfCancellationRequested();
        var success = await _webView.CoreWebView2.PrintToPdfAsync(absolutePath, printSettings);
        if (!success)
        {
            throw new InvalidOperationException("PDF 生成失败。");
        }

        if (!File.Exists(absolutePath) || new FileInfo(absolutePath).Length == 0)
        {
            throw new InvalidOperationException("PDF 生成失败：输出文件为空。");
        }
    }

    /// <summary>将异步操作调度到 WPF UI 线程</summary>
    private static Task RunOnUiThreadAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        var dispatcher = Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("PDF 导出需要在 WPF 应用上下文中运行。");

        if (dispatcher.CheckAccess())
        {
            return action();
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.BeginInvoke(DispatcherPriority.Normal, async () =>
        {
            try
            {
                await action();
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        }

        return tcs.Task;
    }

    /// <summary>删除导出过程中使用的临时 HTML 文件</summary>
    private static void TryDeleteTempHtml(string? tempHtmlPath, string? tempHtmlDir)
    {
        try
        {
            if (!string.IsNullOrEmpty(tempHtmlPath) && File.Exists(tempHtmlPath))
            {
                File.Delete(tempHtmlPath);
            }

            if (!string.IsNullOrEmpty(tempHtmlDir) && Directory.Exists(tempHtmlDir))
            {
                Directory.Delete(tempHtmlDir, recursive: true);
            }
        }
        catch
        {
            // 临时文件清理失败不影响导出结果
        }
    }

    /// <summary>确认 PDF 导出宿主尚未进入最终释放阶段</summary>
    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            throw new ObjectDisposedException(nameof(WebView2PdfExportHost));
        }
    }

    /// <summary>关闭隐藏窗口并释放 WebView2 导出资源</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        // 等待进行中的导出完成，避免在 WebView2 操作中途关闭宿主窗口。
        if (!_exportGate.Wait(TimeSpan.FromSeconds(5)))
        {
            return;
        }

        _webView?.Dispose();
        _webView = null;

        if (_hostWindow != null)
        {
            _hostWindow.Content = null;
            _hostWindow.Close();
            _hostWindow = null;
        }

        _environment = null;
        _initialized = false;
        _exportGate.Dispose();
    }
}

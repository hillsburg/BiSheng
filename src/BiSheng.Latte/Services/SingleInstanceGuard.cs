using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace BiSheng.Latte.Services;

/// <summary>
/// 单实例守卫：同一用户数据目录只允许一个 Latte 进程运行。
/// 多个进程同时读写 local.db、各自运行同步引擎与启动备份，会产生假冲突甚至损坏数据库。
/// 后启动的实例通过命名事件唤醒已运行实例的主窗口，然后自行退出。
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    /// <summary>
    /// 未拿到锁时额外等待的时长：覆盖「旧实例正在退出（同步、备份）时用户又启动」的情况，
    /// 旧实例在此期间退出则新实例接管，否则视为旧实例仍在运行。
    /// </summary>
    private static readonly TimeSpan HandoverWait = TimeSpan.FromSeconds(5);

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateEvent;
    private readonly ManualResetEvent _stopListening = new(false);
    private Thread? _listener;
    private bool _ownsMutex;
    private bool _disposed;

    /// <summary>其他实例请求激活主窗口（在后台线程触发，订阅方需自行切回 UI 线程）</summary>
    public event Action? ActivationRequested;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle activateEvent, bool ownsMutex)
    {
        _mutex = mutex;
        _activateEvent = activateEvent;
        _ownsMutex = ownsMutex;
    }

    /// <summary>
    /// 尝试成为主实例。返回 null 表示已有实例在运行且已通知其激活，调用方应直接退出。
    /// 必须在 STA 主线程调用，且由同一线程 <see cref="Dispose"/>（Mutex 归属于获取它的线程）。
    /// </summary>
    public static SingleInstanceGuard? TryAcquire() => TryAcquire(LatteAppPaths.Root, HandoverWait);

    /// <summary>
    /// 显式指定数据根与交接等待（测试用）：不读全局路径覆盖，
    /// 避免并行测试误用真实数据目录而去唤醒本机正在运行的 Latte。
    /// </summary>
    internal static SingleInstanceGuard? TryAcquire(string dataRoot, TimeSpan handoverWait)
    {
        var baseName = BuildNameForDataRoot(dataRoot);
        var mutex = new Mutex(initiallyOwned: false, $@"Local\{baseName}.Instance");
        var activateEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            $@"Local\{baseName}.Activate");

        if (TryWait(mutex, TimeSpan.Zero))
        {
            return new SingleInstanceGuard(mutex, activateEvent, ownsMutex: true);
        }

        // 先唤醒已运行实例；若它其实正在退出，再给它一点时间释放锁
        activateEvent.Set();
        if (TryWait(mutex, handoverWait))
        {
            // 旧实例已退出：清掉刚才的激活信号，避免本实例启动后被误唤醒
            activateEvent.Reset();
            return new SingleInstanceGuard(mutex, activateEvent, ownsMutex: true);
        }

        activateEvent.Dispose();
        mutex.Dispose();
        return null;
    }

    /// <summary>开始监听其他实例的激活请求</summary>
    public void StartListening()
    {
        if (_listener != null || _disposed)
        {
            return;
        }

        _listener = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = "Latte.SingleInstanceListener"
        };
        _listener.Start();
    }

    private void ListenLoop()
    {
        var handles = new WaitHandle[] { _activateEvent, _stopListening };
        while (true)
        {
            var index = WaitHandle.WaitAny(handles);
            if (index != 0)
            {
                return;
            }

            try
            {
                ActivationRequested?.Invoke();
            }
            catch (Exception ex)
            {
                LogHelper.Warn("处理激活请求失败: {0}", ex.Message);
            }
        }
    }

    /// <summary>
    /// 等待互斥量；前一个持有者异常终止（未释放）时 WaitOne 抛 AbandonedMutexException，
    /// 但所有权已转移给当前线程，视为获取成功。
    /// </summary>
    private static bool TryWait(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    /// <summary>按数据目录生成内核对象名：开发环境改了数据根时不与正式实例互斥</summary>
    private static string BuildNameForDataRoot(string root)
    {
        var normalized = root.TrimEnd('\\', '/').ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return "BiSheng.Latte." + Convert.ToHexString(hash, 0, 8);
    }

    /// <summary>停止监听并释放互斥量（须在获取锁的线程调用）</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopListening.Set();
        _listener?.Join(TimeSpan.FromSeconds(1));

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 非持有线程释放会抛异常；进程退出时内核仍会回收，不影响下次启动
            }

            _ownsMutex = false;
        }

        _mutex.Dispose();
        _activateEvent.Dispose();
        _stopListening.Dispose();
    }
}

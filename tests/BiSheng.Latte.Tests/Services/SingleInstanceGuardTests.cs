using System.IO;
using System.Threading;
using BiSheng.Latte.Services;

namespace BiSheng.Latte.Tests.Services;

/// <summary>
/// 单实例守卫：Mutex 归属线程，因此每个「实例」都在独立线程上获取与释放，模拟两个进程。
/// 数据根使用随机临时路径，保证内核对象名与本机真实 Latte 不冲突。
/// </summary>
public sealed class SingleInstanceGuardTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly string _dataRoot =
        Path.Combine(Path.GetTempPath(), "bisheng-instance-" + Guid.NewGuid().ToString("N"));

    /// <summary>已有实例时第二个实例拿不到锁，且已有实例收到激活请求</summary>
    [Fact]
    public void SecondInstance_IsRejected_AndActivatesFirst()
    {
        using var activated = new ManualResetEventSlim(false);
        using var firstHolder = new InstanceThread(_dataRoot, guard =>
        {
            guard.ActivationRequested += () => activated.Set();
            guard.StartListening();
        });

        var second = AcquireOnNewThread(TimeSpan.Zero);

        Assert.Null(second);
        Assert.True(activated.Wait(Timeout), "已运行实例应收到激活请求");
    }

    /// <summary>旧实例释放后，新实例可以正常接管</summary>
    [Fact]
    public void AfterFirstReleases_NewInstanceAcquires()
    {
        var first = new InstanceThread(_dataRoot, _ => { });
        first.Dispose();

        var next = AcquireOnNewThread(TimeSpan.Zero);

        Assert.NotNull(next);
        ReleaseHandles(next!);
    }

    /// <summary>交接等待期间旧实例退出（正在关闭时用户又启动），新实例接管而不是直接退出</summary>
    [Fact]
    public void FirstExitsDuringHandover_NewInstanceTakesOver()
    {
        var first = new InstanceThread(_dataRoot, _ => { });
        var releaseTimer = new Timer(_ => first.Dispose(), null, TimeSpan.FromMilliseconds(200), System.Threading.Timeout.InfiniteTimeSpan);

        var next = AcquireOnNewThread(Timeout);
        releaseTimer.Dispose();

        Assert.NotNull(next);
        ReleaseHandles(next!);
    }

    /// <summary>在一次性线程上获取守卫，模拟新启动的进程</summary>
    private SingleInstanceGuard? AcquireOnNewThread(TimeSpan handoverWait)
    {
        SingleInstanceGuard? result = null;
        var thread = new Thread(() =>
        {
            result = SingleInstanceGuard.TryAcquire(_dataRoot, handoverWait);
        });
        thread.Start();
        thread.Join();
        return result;
    }

    /// <summary>
    /// 获取线程已结束，Mutex 已变为 abandoned（内核回收所有权）；
    /// 这里只释放句柄，跨线程 ReleaseMutex 的异常由守卫内部吞掉。
    /// </summary>
    private static void ReleaseHandles(SingleInstanceGuard guard)
    {
        guard.Dispose();
    }

    /// <summary>在专属线程上持有守卫，直到 Dispose 时在同一线程释放</summary>
    private sealed class InstanceThread : IDisposable
    {
        private readonly ManualResetEventSlim _acquired = new(false);
        private readonly ManualResetEventSlim _release = new(false);
        private readonly Thread _thread;
        private bool _disposed;

        public InstanceThread(string dataRoot, Action<SingleInstanceGuard> onAcquired)
        {
            _thread = new Thread(() =>
            {
                using var guard = SingleInstanceGuard.TryAcquire(dataRoot, TimeSpan.Zero);
                Assert.NotNull(guard);
                onAcquired(guard!);
                _acquired.Set();
                _release.Wait();
            })
            {
                IsBackground = true
            };
            _thread.Start();
            Assert.True(_acquired.Wait(Timeout), "首个实例应能拿到锁");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _release.Set();
            _thread.Join(Timeout);
            _acquired.Dispose();
            _release.Dispose();
        }
    }
}

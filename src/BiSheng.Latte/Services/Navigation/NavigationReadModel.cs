using System.Windows;
using System.Windows.Threading;

namespace BiSheng.Latte.Services.Navigation;

/// <summary>内存导航读模型：Sync 层、本地 CRUD 与展示层的唯一边界</summary>
public sealed class NavigationReadModel : INavigationReadModel
{
    /// <inheritdoc />
    public event Action<NavigationProjectionUpdate>? Changed;

    /// <inheritdoc />
    public void Publish(NavigationProjectionUpdate update, bool waitForPresentation = false)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            Changed?.Invoke(update);
            return;
        }

        if (waitForPresentation)
        {
            // 非 UI 的 STA（如 xUnit StaFact）若 Invoke 到“建在其他线程且无消息泵”的 Dispatcher 会死锁。
            // 生产后台投递一般为 MTA，仍走同步 Invoke；带超时避免 CI 永久挂起。
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                Changed?.Invoke(update);
                return;
            }

            dispatcher.Invoke(
                () => Changed?.Invoke(update),
                DispatcherPriority.Send,
                CancellationToken.None,
                TimeSpan.FromSeconds(30));
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Background, () => Changed?.Invoke(update));
    }
}

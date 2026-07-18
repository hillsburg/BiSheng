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
            dispatcher.Invoke(() => Changed?.Invoke(update));
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Background, () => Changed?.Invoke(update));
    }
}

using BiSheng.Latte.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace BiSheng.Latte.Composition;

/// <summary>WPF 应用组合根：构建并持有 ServiceProvider</summary>
public static class LatteHost
{
    private static IServiceProvider? _services;

    /// <summary>已构建的服务容器；Build 之后可用</summary>
    public static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("LatteHost 尚未 Build");

    /// <summary>构建 DI 容器（应用生命周期内调用一次）</summary>
    public static void Build(Action<IServiceCollection>? configure = null)
    {
        if (_services != null)
        {
            return;
        }

        var collection = new ServiceCollection();
        collection.AddBiShengLatte();
        configure?.Invoke(collection);
        _services = collection.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    /// <summary>解析服务</summary>
    public static T GetRequiredService<T>() where T : notnull =>
        Services.GetRequiredService<T>();

    /// <summary>释放应用服务容器及其创建的单例资源</summary>
    public static void Dispose()
    {
        var services = Interlocked.Exchange(ref _services, null);
        if (services is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

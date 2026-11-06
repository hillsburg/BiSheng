using BiSheng.Latte.Services;
using Velopack;

namespace BiSheng.Latte;

/// <summary>
/// 自定义入口：先交给 Velopack 处理安装/更新钩子，再启动 WPF。
/// </summary>
public static class Program
{
    /// <summary>应用程序入口</summary>
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        // Velopack 钩子之后再抢单实例锁：安装/更新钩子进程不能被当成第二个实例拦掉
        using var instanceGuard = SingleInstanceGuard.TryAcquire();
        if (instanceGuard == null)
        {
            return;
        }

        var app = new App { InstanceGuard = instanceGuard };
        app.InitializeComponent();
        app.Run();
    }
}

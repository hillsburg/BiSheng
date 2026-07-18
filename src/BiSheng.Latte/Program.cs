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

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}

using BiSheng.Latte.Tests.Fixtures;

namespace BiSheng.Latte.Tests;

/// <summary>共享 STA 线程与 WPF Application，避免 StaFact 测试类切换时死锁</summary>
[CollectionDefinition("WpfSta", DisableParallelization = true)]
public sealed class WpfStaCollection : ICollectionFixture<WpfStaFixture>
{
}

/// <summary>在 STA 集合内初始化一次 WPF Application</summary>
public sealed class WpfStaFixture
{
    /// <summary>构造并确保 Application 存在</summary>
    public WpfStaFixture()
    {
        LatteTestDbFactory.EnsureWpfApplication();
    }
}

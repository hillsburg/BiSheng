namespace BiSheng.Latte.Data;

/// <summary>本地 SQLite 上下文工厂（组合根单例，便于测试替换）</summary>
public interface ILocalDbContextFactory
{
    /// <summary>创建新的短生命周期 DbContext</summary>
    LocalDbContext Create();
}

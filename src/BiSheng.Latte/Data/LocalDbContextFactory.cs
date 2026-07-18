namespace BiSheng.Latte.Data;

/// <summary>默认本地 DbContext 工厂</summary>
public sealed class LocalDbContextFactory : ILocalDbContextFactory
{
    /// <inheritdoc />
    public LocalDbContext Create() => new LocalDbContext();
}

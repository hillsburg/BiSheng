using System.IO;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Latte.Data;

/// <summary>
/// 本地 SQLite 数据库上下文
///
/// 并发保护策略：
/// 1. WAL 模式：读写不互斥，提高并发性能
/// 2. 全局 SemaphoreSlim：多个 DbContext 实例共享同一写入锁，
///    防止并发 SaveChanges 导致 "database is locked" 错误
/// </summary>
public class LocalDbContext : DbContext
{
    public DbSet<LocalFolder> Folders => Set<LocalFolder>();
    public DbSet<LocalNote> Notes => Set<LocalNote>();
    public DbSet<LocalImage> Images => Set<LocalImage>();
    public DbSet<LocalSyncState> SyncState => Set<LocalSyncState>();
    public DbSet<SyncConflict> SyncConflicts => Set<SyncConflict>();
    public DbSet<LocalPendingChange> PendingChanges => Set<LocalPendingChange>();
    public DbSet<LocalNoteRevision> NoteRevisions => Set<LocalNoteRevision>();
    public DbSet<LocalEditJournalEntry> EditJournal => Set<LocalEditJournalEntry>();

    private readonly string _dbPath;

    /// <summary>全局写入锁：所有 LocalDbContext 实例共享</summary>
    private static readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>是否已初始化 WAL 模式（进程级标志，只需执行一次）</summary>
    private static bool _walInitialized;

    public LocalDbContext()
    {
        _dbPath = LatteAppPaths.DatabaseFile;
    }

    public LocalDbContext(DbContextOptions<LocalDbContext> options) : base(options)
    {
        _dbPath = LatteAppPaths.DatabaseFile;
    }

    /// <summary>测试专用：共享 in-memory 连接，由 BiSheng.Latte.Tests 设置</summary>
    internal static SqliteConnection? TestConnection { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            if (TestConnection != null)
            {
                optionsBuilder.UseSqlite(TestConnection);
                return;
            }

            optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        }
    }

    /// <summary>初始化 WAL 模式（进程首次创建 DbContext 时执行）</summary>
    public void InitializeWalMode()
    {
        if (_walInitialized) return;
        _walInitialized = true;

        try
        {
            Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL");
            Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000");
        }
        catch { /* WAL 设置失败时回退到默认 journal 模式 */ }
    }

    public async Task<int> SaveChangesWithLockAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            return await SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public int SaveChangesWithLock()
    {
        _writeLock.Wait();
        try
        {
            return SaveChanges();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<LocalFolder>(e =>
        {
            e.HasIndex(f => f.IsDeleted);
            e.HasIndex(f => f.ParentId);
            e.HasIndex(f => f.DeletedAt);
        });

        modelBuilder.Entity<LocalNote>(e =>
        {
            e.HasIndex(n => new { n.FolderId, n.IsDeleted });
            e.HasIndex(n => n.UpdatedAt);
            e.HasIndex(n => n.DeletedAt);
        });

        modelBuilder.Entity<LocalEditJournalEntry>(e =>
        {
            e.HasIndex(j => new { j.EntityType, j.EntityId, j.CreatedAtUtc });
            e.HasIndex(j => j.CreatedAtUtc);
        });

        modelBuilder.Entity<LocalPendingChange>(e =>
        {
            e.HasIndex(p => new { p.EntityType, p.EntityId }).IsUnique();
        });

        modelBuilder.Entity<LocalImage>(e =>
        {
            e.HasIndex(i => i.NoteId);
            e.HasIndex(i => i.Synced);
        });

        modelBuilder.Entity<LocalNoteRevision>(e =>
        {
            e.HasIndex(r => new { r.NoteId, r.RevisionNumber });
            e.HasIndex(r => new { r.NoteId, r.CreatedAt });
        });
    }
}

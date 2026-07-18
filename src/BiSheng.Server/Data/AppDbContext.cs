using BiSheng.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<ServerConfig> ServerConfigs => Set<ServerConfig>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<SyncLog> SyncLogs => Set<SyncLog>();
    public DbSet<ClientSyncState> ClientSyncStates => Set<ClientSyncState>();
    public DbSet<UserSyncMeta> UserSyncMetas => Set<UserSyncMeta>();
    public DbSet<ServerImage> Images => Set<ServerImage>();
    public DbSet<NoteRevision> NoteRevisions => Set<NoteRevision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
        });

        // Folder
        modelBuilder.Entity<Folder>(e =>
        {
            e.HasIndex(f => new { f.UserId, f.Version });
            e.HasOne(f => f.Parent)
                .WithMany(f => f.Children)
                .HasForeignKey(f => f.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(f => f.User)
                .WithMany(u => u.Folders)
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Note
        modelBuilder.Entity<Note>(e =>
        {
            e.HasIndex(n => new { n.UserId, n.Version });
            e.HasIndex(n => new { n.FolderId, n.IsDeleted });
            e.HasOne(n => n.Folder)
                .WithMany(f => f.Notes)
                .HasForeignKey(n => n.FolderId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(n => n.User)
                .WithMany(u => u.Notes)
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ApiKey
        modelBuilder.Entity<ApiKey>(e =>
        {
            e.HasIndex(k => k.KeyValue).IsUnique();
            e.HasIndex(k => k.UserId);
            e.HasOne(k => k.User)
                .WithMany()
                .HasForeignKey(k => k.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // SyncLog
        modelBuilder.Entity<SyncLog>(e =>
        {
            e.HasIndex(s => new { s.UserId, s.Version }).IsUnique();
            e.HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClientSyncState>(e =>
        {
            e.HasIndex(c => c.UserId);
            e.HasIndex(c => new { c.UserId, c.IsStaleExcluded });
            e.HasOne(c => c.ApiKey)
                .WithOne()
                .HasForeignKey<ClientSyncState>(c => c.ApiKeyId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.User)
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserSyncMeta>(e =>
        {
            e.HasOne(m => m.User)
                .WithOne()
                .HasForeignKey<UserSyncMeta>(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ServerImage
        modelBuilder.Entity<ServerImage>(e =>
        {
            e.HasIndex(i => new { i.UserId, i.IsDeleted });
            e.HasIndex(i => i.DeletedAt);
            e.HasOne(i => i.User)
                .WithMany()
                .HasForeignKey(i => i.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NoteRevision>(e =>
        {
            e.HasIndex(r => new { r.NoteId, r.RevisionNumber }).IsUnique();
            e.HasIndex(r => new { r.UserId, r.NoteId, r.CreatedAt });
            e.HasOne(r => r.Note)
                .WithMany()
                .HasForeignKey(r => r.NoteId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

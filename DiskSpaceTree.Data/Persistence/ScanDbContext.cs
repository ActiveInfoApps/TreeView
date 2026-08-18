using Microsoft.EntityFrameworkCore;
using DiskSpaceTree.Data.Entities;

namespace DiskSpaceTree.Data.Persistence;

public sealed class ScanDbContext : DbContext
{
    public DbSet<Execution> Executions => Set<Execution>();
    public DbSet<Entities.Directory> Directories => Set<Entities.Directory>();
    public DbSet<Entities.DirectoryInfo> DirectoryInfos => Set<Entities.DirectoryInfo>();

    private readonly string _dbPath;

    public ScanDbContext()
    {
        _dbPath = Path.Combine("C:", "TreeView", "Database", "scanhistory.db");
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Execution>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.RootPath).IsRequired().HasMaxLength(1024);
        });

        modelBuilder.Entity<Entities.Directory>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Path).IsRequired().HasMaxLength(2048);
            e.Property(x => x.Name).IsRequired().HasMaxLength(260);
            e.HasIndex(x => x.Path).IsUnique();
            e.HasOne(x => x.ParentDirectory)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentDirectoryId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.FirstSeenExecution)
                .WithMany(x => x.FirstSeenDirectories)
                .HasForeignKey(x => x.FirstSeenExecutionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.LastSeenExecution)
                .WithMany(x => x.LastSeenDirectories)
                .HasForeignKey(x => x.LastSeenExecutionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DeletedAtExecution)
                .WithMany(x => x.DeletedAtDirectories)
                .HasForeignKey(x => x.DeletedAtExecutionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Entities.DirectoryInfo>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Directory)
                .WithMany(x => x.Infos)
                .HasForeignKey(x => x.DirectoryId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Execution)
                .WithMany(x => x.DirectoryInfos)
                .HasForeignKey(x => x.ExecutionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.DirectoryId, x.ExecutionId }).IsUnique();
        });
    }
}

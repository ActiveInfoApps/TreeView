namespace DiskSpaceTree.Data.Entities;

public sealed class Execution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RootPath { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long TotalDirectoriesFound { get; set; }
    public long TotalFilesProcessed { get; set; }

    public List<Directory> FirstSeenDirectories { get; set; } = new();
    public List<Directory> LastSeenDirectories { get; set; } = new();
    public List<Directory> DeletedAtDirectories { get; set; } = new();
    public List<DirectoryInfo> DirectoryInfos { get; set; } = new();
}

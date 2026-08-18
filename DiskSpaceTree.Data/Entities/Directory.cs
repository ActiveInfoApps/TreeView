namespace DiskSpaceTree.Data.Entities;

public sealed class Directory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public Guid? ParentDirectoryId { get; set; }
    public Directory? ParentDirectory { get; set; }
    public List<Directory> Children { get; set; } = new();

    public Guid FirstSeenExecutionId { get; set; }
    public Execution FirstSeenExecution { get; set; } = null!;

    public Guid LastSeenExecutionId { get; set; }
    public Execution LastSeenExecution { get; set; } = null!;

    public bool IsDeleted { get; set; }

    public Guid? DeletedAtExecutionId { get; set; }
    public Execution? DeletedAtExecution { get; set; }

    public List<DirectoryInfo> Infos { get; set; } = new();
}

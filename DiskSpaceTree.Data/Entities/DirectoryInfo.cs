namespace DiskSpaceTree.Data.Entities;

public sealed class DirectoryInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DirectoryId { get; set; }
    public Directory Directory { get; set; } = null!;

    public Guid ExecutionId { get; set; }
    public Execution Execution { get; set; } = null!;

    public long SizeInKb { get; set; }
    public long FileCount { get; set; }
    public long DirectSizeInKb { get; set; }
    public long DirectFileCount { get; set; }
    public bool HasError { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsDeletedSnapshot { get; set; }
}

namespace DiskSpaceTree.Data.Persistence;

public sealed class ExecutionDto
{
    public Guid Id { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string RootPath { get; set; } = string.Empty;
    public long TotalDirectoriesFound { get; set; }

    public string DisplayText => CompletedAt.HasValue
        ? $"{CompletedAt:yyyy-MM-dd HH:mm:ss} — {RootPath} ({TotalDirectoriesFound:N0} dirs)"
        : $"{RootPath} (in progress)";
}

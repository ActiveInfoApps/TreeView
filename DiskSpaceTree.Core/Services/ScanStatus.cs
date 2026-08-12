namespace DiskSpaceTree.Services;

public sealed class ScanStatus
{
    public ScanStatus(string currentFilePath, long filesProcessed, ScanStage stage, long directoriesFound, long directoriesScanned)
    {
        CurrentFilePath = currentFilePath;
        FilesProcessed = filesProcessed;
        Stage = stage;
        DirectoriesFound = directoriesFound;
        DirectoriesScanned = directoriesScanned;
    }

    public string CurrentFilePath { get; }

    public long FilesProcessed { get; }

    public ScanStage Stage { get; }

    /// <summary>Total number of directories discovered during the listing stage.</summary>
    public long DirectoriesFound { get; }

    /// <summary>Number of directories whose files have been scanned so far.</summary>
    public long DirectoriesScanned { get; }
}
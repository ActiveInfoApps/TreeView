namespace DiskSpaceTree.Services;

public sealed class ScanStatus
{
    public ScanStatus(string currentFilePath, long filesProcessed)
    {
        CurrentFilePath = currentFilePath;
        FilesProcessed = filesProcessed;
    }

    public string CurrentFilePath { get; }

    public long FilesProcessed { get; }
}

namespace DiskSpaceTree.Data.Persistence;

public sealed class DirectoryChangeDto
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PreviousSizeInKb { get; set; }
    public long PreviousFileCount { get; set; }
    public long CurrentSizeInKb { get; set; }
    public long CurrentFileCount { get; set; }
    public long SizeChangeInKb { get; set; }
}

namespace DiskSpaceTree.Services;

public sealed class FileSystemAccessor : IFileSystemAccessor
{
    public IEnumerable<string> EnumerateFiles(string path)
    {
        return Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly);
    }

    public IEnumerable<string> EnumerateDirectories(string path)
    {
        return Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly);
    }

    public long GetFileLength(string path)
    {
        return new FileInfo(path).Length;
    }
}

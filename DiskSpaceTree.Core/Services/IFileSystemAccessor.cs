namespace DiskSpaceTree.Services;

public interface IFileSystemAccessor
{
    IEnumerable<string> EnumerateFiles(string path);

    IEnumerable<string> EnumerateDirectories(string path);

    long GetFileLength(string path);
}

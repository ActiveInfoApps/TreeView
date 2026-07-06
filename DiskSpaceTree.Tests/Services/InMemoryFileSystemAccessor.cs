using DiskSpaceTree.Services;

namespace DiskSpaceTree.Tests.Services;

public sealed class InMemoryFileSystemAccessor : IFileSystemAccessor
{
    private readonly Dictionary<string, List<string>> _directories = new();
    private readonly Dictionary<string, List<string>> _files = new();
    private readonly Dictionary<string, long> _fileLengths = new();
    private readonly Dictionary<string, Exception> _fileErrors = new();
    private readonly Dictionary<string, Exception> _directoryErrors = new();

    public void AddDirectory(string path)
    {
        _directories[path] = new List<string>();
        _files[path] = new List<string>();
    }

    public void AddFile(string directoryPath, string fileName, long length)
    {
        if (!_files.ContainsKey(directoryPath))
        {
            AddDirectory(directoryPath);
        }

        var filePath = System.IO.Path.Combine(directoryPath, fileName);
        _files[directoryPath].Add(filePath);
        _fileLengths[filePath] = length;
    }

    public void AddChildDirectory(string parentPath, string childName)
    {
        if (!_directories.ContainsKey(parentPath))
        {
            AddDirectory(parentPath);
        }

        var childPath = System.IO.Path.Combine(parentPath, childName);
        _directories[parentPath].Add(childPath);

        if (!_directories.ContainsKey(childPath))
        {
            AddDirectory(childPath);
        }
    }

    public void ThrowOnEnumerateFiles(string path, Exception exception)
    {
        _directoryErrors[path] = exception;
    }

    public void ThrowOnEnumerateDirectories(string path, Exception exception)
    {
        _directoryErrors[path] = exception;
    }

    public void ThrowOnFileLength(string path, Exception exception)
    {
        _fileErrors[path] = exception;
    }

    public IEnumerable<string> EnumerateFiles(string path)
    {
        if (_directoryErrors.TryGetValue(path, out var error))
        {
            throw error;
        }

        if (_files.TryGetValue(path, out var files))
        {
            return files;
        }

        throw new DirectoryNotFoundException($"Directory not found: {path}");
    }

    public IEnumerable<string> EnumerateDirectories(string path)
    {
        if (_directoryErrors.TryGetValue(path, out var error))
        {
            throw error;
        }

        if (_directories.TryGetValue(path, out var directories))
        {
            return directories;
        }

        throw new DirectoryNotFoundException($"Directory not found: {path}");
    }

    public long GetFileLength(string path)
    {
        if (_fileErrors.TryGetValue(path, out var error))
        {
            throw error;
        }

        if (_fileLengths.TryGetValue(path, out var length))
        {
            return length;
        }

        throw new FileNotFoundException($"File not found: {path}");
    }
}

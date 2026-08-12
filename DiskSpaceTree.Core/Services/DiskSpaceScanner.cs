using DiskSpaceTree.Diagnostics;
using DiskSpaceTree.Models;

namespace DiskSpaceTree.Services;

public sealed class DiskSpaceScanner
{
    private readonly IFileSystemAccessor _fileSystemAccessor;
    private long _filesProcessed;

    public DiskSpaceScanner(IFileSystemAccessor fileSystemAccessor)
    {
        _fileSystemAccessor = fileSystemAccessor ?? throw new ArgumentNullException(nameof(fileSystemAccessor));
    }

    public static IEnumerable<FileSystemNode> GetDrives()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
            {
                continue;
            }

            var node = new FileSystemNode(drive.Name, drive.RootDirectory.FullName, isDirectory: true)
            {
                SizeInKb = 0,
                HasError = false
            };

            yield return node;
        }
    }

    public async Task ScanDriveAsync(
        FileSystemNode driveNode,
        IProgress<ScanStatus>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _filesProcessed, 0);
        await ScanDirectoryAsync(driveNode, progress, cancellationToken);
    }

    public async Task ScanDirectoryAsync(
        FileSystemNode node,
        IProgress<ScanStatus>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await ScanDirectoryRecursiveAsync(node, progress, cancellationToken);
    }

    private async Task ScanDirectoryRecursiveAsync(FileSystemNode node, IProgress<ScanStatus>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Logger.Log($"Scan start: {node.Path}");

        long sizeInBytes = 0;

        try
        {
            foreach (var file in _fileSystemAccessor.EnumerateFiles(node.Path))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    sizeInBytes += _fileSystemAccessor.GetFileLength(file);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or FileNotFoundException or PathTooLongException)
                {
                    Logger.Log($"File error: {file} -> {ex.GetType().Name}");
                }

                var processed = Interlocked.Increment(ref _filesProcessed);
                if (processed % 100 == 0)
                {
                    progress?.Report(new ScanStatus(file, processed));
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PathTooLongException or DirectoryNotFoundException)
        {
            node.HasError = true;
            node.ErrorMessage = ex.Message;
            Logger.Log($"Directory error: {node.Path} -> {ex.GetType().Name}: {ex.Message}");
        }

        Logger.Log($"Files scanned: {node.Path} -> {sizeInBytes} bytes");

        try
        {
            foreach (var directory in _fileSystemAccessor.EnumerateDirectories(node.Path))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var childName = System.IO.Path.GetFileName(directory);
                var childNode = new FileSystemNode(childName, directory, isDirectory: true)
                {
                    Parent = node
                };

                await ScanDirectoryRecursiveAsync(childNode, progress, cancellationToken);
                sizeInBytes += childNode.SizeInKb * 1024;
                node.SizeInKb = ConvertToKilobytes(sizeInBytes);
                Logger.Log($"Child accumulated: {node.Path} <- {childNode.Path} ({childNode.SizeInKb} KB) => running total {sizeInBytes} bytes");

                lock (node.SyncRoot)
                {
                    InsertChildSorted(node, childNode);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PathTooLongException or DirectoryNotFoundException)
        {
            node.HasError = true;
            node.ErrorMessage = ex.Message;
            Logger.Log($"Directory error: {node.Path} -> {ex.GetType().Name}: {ex.Message}");
        }

        node.SizeInKb = ConvertToKilobytes(sizeInBytes);
        Logger.Log($"Scan complete: {node.Path} -> {node.SizeInKb} KB");
        node.RaiseEvent();
    }

    private static long ConvertToKilobytes(long bytes)
    {
        return (bytes + 1023) / 1024;
    }

    private static void InsertChildSorted(FileSystemNode parent, FileSystemNode child)
    {
        var index = 0;
        while (index < parent.Children.Count && parent.Children[index].SizeInKb > child.SizeInKb)
        {
            index++;
        }

        parent.Children.Insert(index, child);
    }

}

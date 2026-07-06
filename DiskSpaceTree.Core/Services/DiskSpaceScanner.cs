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

    /// <summary>
    /// When true, each child directory is added to its parent before it is scanned
    /// and moved to the correct sorted position after the scan completes.
    /// </summary>
    public bool AddDirectoriesBeforeScan { get; set; }

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

        Thread.Sleep(100);

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
                    // Ignore files we cannot read.
                }

                // Update size periodically so the UI shows live progress.
                node.SizeInKb = ConvertToKilobytes(sizeInBytes);

                // Report status so the UI can show the current file and processed count.
                var processed = Interlocked.Increment(ref _filesProcessed);
                progress?.Report(new ScanStatus(file, processed));

                // Yield frequently so the UI can render between files.
                await Task.Yield();
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PathTooLongException or DirectoryNotFoundException)
        {
            node.HasError = true;
            node.ErrorMessage = ex.Message;
        }

        try
        {
            foreach (var directory in _fileSystemAccessor.EnumerateDirectories(node.Path))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var childName = System.IO.Path.GetFileName(directory);
                var childNode = new FileSystemNode(childName, directory, isDirectory: true);

                if (AddDirectoriesBeforeScan)
                {
                    node.Children.Add(childNode);
                }

                await ScanDirectoryRecursiveAsync(childNode, progress, cancellationToken);

                sizeInBytes += childNode.SizeInKb * 1024;
                node.SizeInKb = ConvertToKilobytes(sizeInBytes);

                if (AddDirectoriesBeforeScan)
                {
                    node.Children.Remove(childNode);
                }

                InsertChildSorted(node, childNode);

                await Task.Yield();
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PathTooLongException or DirectoryNotFoundException)
        {
            node.HasError = true;
            node.ErrorMessage = ex.Message;
        }

        node.SizeInKb = ConvertToKilobytes(sizeInBytes);
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

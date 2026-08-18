using System.Collections.Concurrent;
using DiskSpaceTree.Diagnostics;
using DiskSpaceTree.Models;

namespace DiskSpaceTree.Services;

public sealed class DiskSpaceScanner
{
    /// <summary>Upper limit on the number of directories whose files get scanned.</summary>
    public const int DefaultMaxDirectoriesToScan = 100000000;
    public const int DirectoryScanTaskDepth = 4;

    private readonly IFileSystemAccessor _fileSystemAccessor;
    private readonly ConcurrentDictionary<string, FileSystemNode> _foundDirectories = new();
    private readonly ConcurrentDictionary<string, FileSystemNode> _scannedDirectories = new();
    private readonly object _sortedDirectoriesLock = new();
    private List<FileSystemNode> _sortedDirectories = [];
    private readonly ConcurrentBag<Task> _listingTasks = new();
    private readonly ConcurrentQueue<FileSystemNode> _directoryQueue = new();
    private long _filesProcessed;
    private ScanStage _currentStage;

    /// <summary>Raised after every directory finishes its file scan (stage 2).</summary>
    public event EventHandler<FileSystemNode>? DirectoryCompleted;

    public DiskSpaceScanner(IFileSystemAccessor fileSystemAccessor, int maxDirectoriesToScan = DefaultMaxDirectoriesToScan)
    {
        _fileSystemAccessor = fileSystemAccessor ?? throw new ArgumentNullException(nameof(fileSystemAccessor));
        MaxDirectoriesToScan = maxDirectoriesToScan;
    }

    public int MaxDirectoriesToScan { get; }

    /// <summary>The total number of directories discovered during the listing stage.</summary>
    public long DirectoriesFound => _foundDirectories.Count;

    /// <summary>The number of directories whose files have been scanned so far.</summary>
    public long DirectoriesScanned => _scannedDirectories.Count;

    /// <summary>The scan stage currently being executed.</summary>
    public ScanStage CurrentStage => _currentStage;

    /// <summary>Returns the top scanned directories ranked by their own file size.</summary>
    public IReadOnlyList<FileSystemNode> GetTopDirectories(int count)
    {
        lock (_sortedDirectoriesLock)
        {
            _sortedDirectories.Sort(CompareDirectoriesBySizeDescending);
            return _sortedDirectories.Take(count).ToList();
        }
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
        _foundDirectories.Clear();
        _scannedDirectories.Clear();
        while (!_directoryQueue.IsEmpty)
        {
            _directoryQueue.TryDequeue(out _);
        }

        while (!_listingTasks.IsEmpty)
        {
            _listingTasks.TryTake(out _);
        }

        lock (_sortedDirectoriesLock)
        {
            _sortedDirectories.Clear();
        }

        Interlocked.Exchange(ref _filesProcessed, 0);
        _currentStage = ScanStage.ListingDirectories;

        // Stage 1: build the full directory tree and count every directory found.
        await BuildDirectoryListAsync(node, progress, cancellationToken, depth: 0);

        // Wait for every subtree task dispatched during the listing to finish so the
        // tree is fully built before file scanning begins.
        while (!_listingTasks.IsEmpty)
        {
            var pending = new List<Task>();
            while (_listingTasks.TryTake(out var task))
            {
                pending.Add(task);
            }

            await Task.WhenAll(pending);
        }

        progress?.Report(new ScanStatus(node.Path, _filesProcessed, ScanStage.ListingDirectories, DirectoriesFound, DirectoriesScanned));

        // Stage 2: start a background task that drains the queue of every discovered
        // directory, updating each directory's direct totals and pushing them up to all
        // of its parents so the whole tree accumulates sizes and file counts.
        _currentStage = ScanStage.ScanningFiles;
        var scanTask = Task.Run(() => DrainDirectoryQueueAsync(progress, cancellationToken), cancellationToken);
        await scanTask;
    }

    private async Task DrainDirectoryQueueAsync(IProgress<ScanStatus>? progress, CancellationToken cancellationToken)
    {
        while (_directoryQueue.TryDequeue(out var node))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (node.Parent is not null && _scannedDirectories.Count >= MaxDirectoriesToScan)
            {
                break;
            }

            await ScanSingleDirectoryAsync(node, progress, cancellationToken);
        }
    }

    private Task ScanSingleDirectoryAsync(FileSystemNode node, IProgress<ScanStatus>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Logger.Log($"Scan start: {node.Path}");

        long sizeInBytes = 0;
        long fileCount = 0;

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

                fileCount++;
                var processed = Interlocked.Increment(ref _filesProcessed);
                if (processed % 100 == 0)
                {
                    progress?.Report(new ScanStatus(file, processed, ScanStage.ScanningFiles, DirectoriesFound, DirectoriesScanned));
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PathTooLongException or DirectoryNotFoundException)
        {
            node.HasError = true;
            node.ErrorMessage = ex.Message;
            Logger.Log($"Directory error: {node.Path} -> {ex.GetType().Name}: {ex.Message}");
        }

        Logger.Log($"Files scanned: {node.Path} -> {sizeInBytes} bytes / {fileCount} files");

        // Direct totals (files in this directory only, excluding subdirectories).
        node.DirectSizeInKb = ConvertToKilobytes(sizeInBytes);
        node.DirectFileCount = fileCount;

        // This directory's direct totals flow into its own recursive totals and into
        // every ancestor so parents accumulate the size and file count of all their
        // subdirectories.
        AccumulateToSelfAndAncestors(node, node.DirectSizeInKb, node.DirectFileCount);

        // Record this directory as having its files scanned (the root is the entry
        // point, not a "found" subdirectory, so it is excluded from the scanned count).
        if (node.Parent is not null)
        {
            var existing = _scannedDirectories.TryAdd(node.Path, node);
            lock (_sortedDirectoriesLock)
            {
                if (existing)
                {
                    _sortedDirectories.Add(node);
                }

                // Re-sort the list only after every 100 scanned directories.
                if (_scannedDirectories.Count % 100 == 0)
                {
                    _sortedDirectories.Sort(CompareDirectoriesBySizeDescending);
                }
            }
        }

        var scanned = DirectoriesScanned;
        if (scanned % 50 == 0 && scanned > 0)
        {
            progress?.Report(new ScanStatus(node.Path, _filesProcessed, ScanStage.ScanningFiles, DirectoriesFound, DirectoriesScanned));
        }

        DirectoryCompleted?.Invoke(this, node);

        // Once the last child of a directory finishes, its subtree is complete and its
        // children can be ordered by their (now final) sizes.
        var parent = node.Parent;
        if (parent is not null && Interlocked.Decrement(ref parent.PendingChildCount) == 0)
        {
            SortChildrenBySizeDescending(parent);
        }

        return Task.CompletedTask;
    }

    private static void AccumulateToSelfAndAncestors(FileSystemNode node, long sizeInKb, long fileCount)
    {
        var current = node;
        while (current is not null)
        {
            current.AddSizeInKb(sizeInKb);
            current.AddFileCount(fileCount);
            current = current.Parent;
        }
    }

    private static void SortChildrenBySizeDescending(FileSystemNode parent)
    {
        lock (parent.SyncRoot)
        {
            var sorted = parent.Children.OrderByDescending(child => child.SizeInKb).ToList();
            parent.Children.ReplaceWith(sorted);
        }
    }

    private async Task<long> BuildDirectoryListAsync(FileSystemNode node, IProgress<ScanStatus>? progress,
        CancellationToken cancellationToken, int depth)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_foundDirectories.Count >= MaxDirectoriesToScan)
        {
            return _foundDirectories.Count;
        }

        Logger.Log($"List start: {node.Path}");

        // This node itself is a directory being scanned.
        _foundDirectories[node.Path] = node;
        _directoryQueue.Enqueue(node);

        List<string> directories;
        try
        {
            directories = _fileSystemAccessor.EnumerateDirectories(node.Path).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PathTooLongException or DirectoryNotFoundException)
        {
            node.HasError = true;
            node.ErrorMessage = ex.Message;
            Logger.Log($"Directory list error: {node.Path} -> {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        // At the fan-out level, dispatch a task per subdirectory without waiting on them.
        // The walker keeps moving to the next sibling, so it never blocks on the subtrees.
        var dispatchTasks = depth >= DirectoryScanTaskDepth;

        // Down to the fan-out level, walk level by level; the count limit is then
        // respected because each child fills its found slot before the next is added.
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_foundDirectories.Count >= MaxDirectoriesToScan)
            {
                break;
            }

            var childName = System.IO.Path.GetFileName(directory);
            var childNode = new FileSystemNode(childName, directory, isDirectory: true)
            {
                Parent = node
            };

            lock (node.SyncRoot)
            {
                node.Children.Add(childNode);
                node.PendingChildCount++;
            }

            if (_foundDirectories.Count % 100 == 0)
            {
                progress?.Report(new ScanStatus(string.Empty, _filesProcessed, ScanStage.ListingDirectories, DirectoriesFound, DirectoriesScanned));
            }

            if (dispatchTasks)
            {
                _listingTasks.Add(Task.Run(() => 
                        BuildDirectoryListAsync(childNode, progress, 
                            cancellationToken, depth + 1), cancellationToken));
            }
            else
            {
                await BuildDirectoryListAsync(childNode, progress, cancellationToken, depth + 1);
            }
        }

        Logger.Log($"List complete: {node.Path} -> {_foundDirectories.Count} directories total");
        return _foundDirectories.Count;
    }

    private static int CompareDirectoriesBySizeDescending(FileSystemNode? x, FileSystemNode? y)
    {
        return y?.DirectSizeInKb.CompareTo(x?.DirectSizeInKb ?? 0) ?? 0;
    }

    private static long ConvertToKilobytes(long bytes)
    {
        return (bytes + 1023) / 1024;
    }

}

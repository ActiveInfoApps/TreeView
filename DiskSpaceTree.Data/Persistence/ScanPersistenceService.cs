using Microsoft.EntityFrameworkCore;
using DiskSpaceTree.Data.Entities;
using DiskSpaceTree.Models;

namespace DiskSpaceTree.Data.Persistence;

public sealed class ScanPersistenceService
{
    private readonly Func<ScanDbContext> _contextFactory;

    public ScanPersistenceService(Func<ScanDbContext>? contextFactory = null)
    {
        _contextFactory = contextFactory ?? (() => new ScanDbContext());
    }

    public async Task SaveScanResultsAsync(
        FileSystemNode rootNode,
        DateTime startedAt,
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory();
        await context.Database.MigrateAsync(cancellationToken);

        var execution = new Execution
        {
            RootPath = rootNode.Path,
            StartedAt = startedAt,
            CompletedAt = DateTime.UtcNow,
            TotalDirectoriesFound = rootNode.FileCount,
            TotalFilesProcessed = rootNode.FileCount,
        };
        context.Executions.Add(execution);
        await context.SaveChangesAsync(cancellationToken);

        var foundPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await SaveDirectoryRecursive(context, rootNode, execution, null, foundPaths, cancellationToken);

        await MarkDeletedDirectories(context, execution, foundPaths, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task SaveDirectoryRecursive(
        ScanDbContext context,
        FileSystemNode node,
        Execution execution,
        Guid? parentId,
        HashSet<string> foundPaths,
        CancellationToken cancellationToken)
    {
        foundPaths.Add(node.Path);

        var existing = await context.Directories
            .FirstOrDefaultAsync(d => d.Path == node.Path, cancellationToken);

        Entities.Directory directory;
        if (existing is null)
        {
            directory = new Entities.Directory
            {
                Path = node.Path,
                Name = node.Name,
                ParentDirectoryId = parentId,
                FirstSeenExecutionId = execution.Id,
                LastSeenExecutionId = execution.Id,
                IsDeleted = false,
            };
            context.Directories.Add(directory);
        }
        else
        {
            directory = existing;
            directory.LastSeenExecutionId = execution.Id;
            if (directory.IsDeleted)
            {
                directory.IsDeleted = false;
                directory.DeletedAtExecutionId = null;
            }
        }

        var info = new Entities.DirectoryInfo
        {
            DirectoryId = directory.Id,
            ExecutionId = execution.Id,
            SizeInKb = node.SizeInKb,
            FileCount = node.FileCount,
            DirectSizeInKb = node.DirectSizeInKb,
            DirectFileCount = node.DirectFileCount,
            HasError = node.HasError,
            ErrorMessage = node.ErrorMessage,
            IsDeletedSnapshot = false,
        };
        context.DirectoryInfos.Add(info);

        await context.SaveChangesAsync(cancellationToken);

        var myId = directory.Id;

        foreach (var child in node.Children)
        {
            if (child.IsDirectory)
            {
                await SaveDirectoryRecursive(context, child, execution, myId, foundPaths, cancellationToken);
            }
        }
    }

    private async Task MarkDeletedDirectories(
        ScanDbContext context,
        Execution execution,
        HashSet<string> foundPaths,
        CancellationToken cancellationToken)
    {
        var previouslyActive = await context.Directories
            .Where(d => !d.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var dir in previouslyActive)
        {
            if (foundPaths.Contains(dir.Path))
                continue;

            dir.IsDeleted = true;
            dir.DeletedAtExecutionId = execution.Id;

            var deletedInfo = new Entities.DirectoryInfo
            {
                DirectoryId = dir.Id,
                ExecutionId = execution.Id,
                SizeInKb = 0,
                FileCount = 0,
                DirectSizeInKb = 0,
                DirectFileCount = 0,
                IsDeletedSnapshot = true,
            };
            context.DirectoryInfos.Add(deletedInfo);
        }
    }

    public async Task<List<ExecutionDto>> GetAllExecutionsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory();
        await context.Database.MigrateAsync(cancellationToken);

        return await context.Executions
            .OrderByDescending(e => e.CompletedAt)
            .Select(e => new ExecutionDto
            {
                Id = e.Id,
                CompletedAt = e.CompletedAt,
                RootPath = e.RootPath,
                TotalDirectoriesFound = e.TotalDirectoriesFound,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<DirectoryChangeDto>> GetTopChangedDirectoriesAsync(
        int count = 50,
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory();
        await context.Database.MigrateAsync(cancellationToken);

        var executions = await context.Executions
            .OrderByDescending(e => e.CompletedAt)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (executions.Count < 2)
        {
            return [];
        }

        return await GetChangedDirectoriesAsync(executions[0].Id, executions[1].Id, count, cancellationToken);
    }

    public async Task<List<DirectoryChangeDto>> GetChangedDirectoriesAsync(
        Guid currentExecutionId,
        Guid previousExecutionId,
        int count = 50,
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory();
        await context.Database.MigrateAsync(cancellationToken);

        var currentInfos = await context.DirectoryInfos
            .Where(di => di.ExecutionId == currentExecutionId && !di.IsDeletedSnapshot)
            .Select(di => new { di.DirectoryId, di.SizeInKb, di.FileCount })
            .ToListAsync(cancellationToken);

        var previousInfos = await context.DirectoryInfos
            .Where(di => di.ExecutionId == previousExecutionId && !di.IsDeletedSnapshot)
            .Select(di => new { di.DirectoryId, di.SizeInKb, di.FileCount })
            .ToDictionaryAsync(di => di.DirectoryId, di => di, cancellationToken);

        var currentDirIds = currentInfos.Select(ci => ci.DirectoryId).ToList();
        var directories = await context.Directories
            .Where(d => currentDirIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Path, d.Name })
            .ToDictionaryAsync(d => d.Id, d => d, cancellationToken);

        var results = new List<DirectoryChangeDto>();

        foreach (var current in currentInfos)
        {
            if (!previousInfos.TryGetValue(current.DirectoryId, out var previous))
                continue;

            if (!directories.TryGetValue(current.DirectoryId, out var dir))
                continue;

            var sizeChange = current.SizeInKb - previous.SizeInKb;
            if (sizeChange == 0)
                continue;

            results.Add(new DirectoryChangeDto
            {
                Path = dir.Path,
                Name = dir.Name,
                PreviousSizeInKb = previous.SizeInKb,
                PreviousFileCount = previous.FileCount,
                CurrentSizeInKb = current.SizeInKb,
                CurrentFileCount = current.FileCount,
                SizeChangeInKb = sizeChange,
            });
        }

        return results
            .OrderByDescending(r => Math.Abs(r.SizeChangeInKb))
            .Take(count)
            .ToList();
    }
}

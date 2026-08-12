using DiskSpaceTree.Models;
using DiskSpaceTree.Services;

namespace DiskSpaceTree.Tests.Services;

public class DiskSpaceScannerTests
{
    private static FileSystemNode CreateNode(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        if (string.IsNullOrEmpty(name))
        {
            name = path;
        }

        return new FileSystemNode(name, path, isDirectory: true);
    }

    [Fact]
    public async Task ScanDirectoryAsync_EmptyDirectory_ReturnsZeroSize()
    {
        var fs = new InMemoryFileSystemAccessor();
        fs.AddDirectory(@"C:\root");
        var scanner = new DiskSpaceScanner(fs);
        var result = CreateNode(@"C:\root");

        await scanner.ScanDirectoryAsync(result);

        Assert.Equal(0, result.SizeInKb);
        Assert.Empty(result.Children);
        Assert.False(result.HasError);
    }

    [Fact]
    public async Task ScanDirectoryAsync_FilesInDirectory_ReturnsTotalSizeInKbRoundedUp()
    {
        var fs = new InMemoryFileSystemAccessor();
        fs.AddFile(@"C:\root", "a.txt", 512);
        fs.AddFile(@"C:\root", "b.txt", 1024);
        fs.AddFile(@"C:\root", "c.txt", 1025);
        var scanner = new DiskSpaceScanner(fs);
        var result = CreateNode(@"C:\root");

        await scanner.ScanDirectoryAsync(result);

        // Total bytes = 512 + 1024 + 1025 = 2561 -> rounded up to 3 KB
        Assert.Equal(3, result.SizeInKb);
    }

    [Fact]
    public async Task ScanDirectoryAsync_NestedDirectories_ReturnsRecursiveTotalSize()
    {
        var fs = new InMemoryFileSystemAccessor();
        fs.AddFile(@"C:\root", "root.txt", 2048);
        fs.AddChildDirectory(@"C:\root", "sub");
        fs.AddFile(@"C:\root\sub", "sub.txt", 512);
        fs.AddChildDirectory(@"C:\root\sub", "deep");
        fs.AddFile(@"C:\root\sub\deep", "deep.txt", 1024);
        var scanner = new DiskSpaceScanner(fs);
        var result = CreateNode(@"C:\root");

        await scanner.ScanDirectoryAsync(result);

        // root: 2048 -> 2 KB
        // sub: 512 + 1024 = 1536 -> 2 KB
        // deep: 1024 -> 1 KB
        // total: 2048 + 512 + 1024 = 3584 bytes -> 4 KB
        Assert.Equal(4, result.SizeInKb);
        Assert.Single(result.Children);
        var sub = result.Children[0];
        Assert.Equal(2, sub.SizeInKb);
        Assert.Single(sub.Children);
        var deep = sub.Children[0];
        Assert.Equal(1, deep.SizeInKb);
    }

    [Fact]
    public async Task ScanDirectoryAsync_NestedDirectories_AccumulatesRecursiveFileCount()
    {
        var fs = new InMemoryFileSystemAccessor();
        fs.AddFile(@"C:\root", "root.txt", 2048);
        fs.AddFile(@"C:\root", "root2.txt", 512);
        fs.AddChildDirectory(@"C:\root", "sub");
        fs.AddFile(@"C:\root\sub", "sub.txt", 512);
        fs.AddFile(@"C:\root\sub", "sub2.txt", 512);
        fs.AddChildDirectory(@"C:\root\sub", "deep");
        fs.AddFile(@"C:\root\sub\deep", "deep.txt", 1024);
        var scanner = new DiskSpaceScanner(fs);
        var result = CreateNode(@"C:\root");

        await scanner.ScanDirectoryAsync(result);

        // root: 2 files + sub's 2 + deep's 1 = 5
        // sub: 2 + deep's 1 = 3
        // deep: 1
        Assert.Equal(5, result.FileCount);
        var sub = result.Children[0];
        Assert.Equal(3, sub.FileCount);
        var deep = sub.Children[0];
        Assert.Equal(1, deep.FileCount);
    }

    [Fact]
    public async Task ScanDirectoryAsync_DirectTotalsExcludeSubdirectories()
    {
        var fs = new InMemoryFileSystemAccessor();
        fs.AddFile(@"C:\root", "root.txt", 2048);
        fs.AddFile(@"C:\root", "root2.txt", 512);
        fs.AddChildDirectory(@"C:\root", "sub");
        fs.AddFile(@"C:\root\sub", "sub.txt", 512);
        fs.AddFile(@"C:\root\sub", "sub2.txt", 512);
        fs.AddChildDirectory(@"C:\root\sub", "deep");
        fs.AddFile(@"C:\root\sub\deep", "deep.txt", 1024);
        var scanner = new DiskSpaceScanner(fs);
        var result = CreateNode(@"C:\root");

        await scanner.ScanDirectoryAsync(result);

        // Root directly contains 2 files = 2048 + 512 = 2560 bytes -> 3 KB.
        Assert.Equal(3, result.DirectSizeInKb);
        Assert.Equal(2, result.DirectFileCount);

        // Sub directly contains 2 files = 1024 bytes -> 1 KB, excluding deep.
        var sub = result.Children[0];
        Assert.Equal(1, sub.DirectSizeInKb);
        Assert.Equal(2, sub.DirectFileCount);

        // Deep directly contains 1 file.
        var deep = sub.Children[0];
        Assert.Equal(1, deep.DirectSizeInKb);
        Assert.Equal(1, deep.DirectFileCount);
    }

    [Fact]
    public async Task ScanDirectoryAsync_ChildrenAreSortedBySizeDescending()
    {
        var fs = new InMemoryFileSystemAccessor();
        fs.AddChildDirectory(@"C:\root", "small");
        fs.AddFile(@"C:\root\small", "file.txt", 100);
        fs.AddChildDirectory(@"C:\root", "large");
        fs.AddFile(@"C:\root\large", "file.txt", 10000);
        fs.AddChildDirectory(@"C:\root", "medium");
        fs.AddFile(@"C:\root\medium", "file.txt", 5000);
        var scanner = new DiskSpaceScanner(fs);
        var result = CreateNode(@"C:\root");

        await scanner.ScanDirectoryAsync(result);

        Assert.Equal(3, result.Children.Count);
        Assert.Equal("large", result.Children[0].Name);
        Assert.Equal("medium", result.Children[1].Name);
        Assert.Equal("small", result.Children[2].Name);
    }

    [Fact]
    public async Task ScanDirectoryAsync_DirectoryNotFound_SetsErrorState()
    {
        var fs = new InMemoryFileSystemAccessor();
        var scanner = new DiskSpaceScanner(fs);
        var result = CreateNode(@"C:\missing");

        await scanner.ScanDirectoryAsync(result);

        Assert.True(result.HasError);
        Assert.NotNull(result.ErrorMessage);
        Assert.Equal(0, result.SizeInKb);
    }

    [Fact]
    public async Task ScanDirectoryAsync_UnauthorizedAccessToDirectory_SetsErrorState()
    {
        var fs = new InMemoryFileSystemAccessor();
        fs.AddDirectory(@"C:\root");
        fs.ThrowOnEnumerateFiles(@"C:\root", new UnauthorizedAccessException("Access denied."));
        var scanner = new DiskSpaceScanner(fs);
        var result = CreateNode(@"C:\root");

        await scanner.ScanDirectoryAsync(result);

        Assert.True(result.HasError);
        Assert.Equal("Access denied.", result.ErrorMessage);
    }

    [Fact]
    public async Task ScanDirectoryAsync_FileLengthThrowsUnauthorizedAccess_IgnoresFile()
    {
        var fs = new InMemoryFileSystemAccessor();
        fs.AddFile(@"C:\root", "accessible.txt", 1024);
        fs.AddFile(@"C:\root", "locked.txt", 9999);
        fs.ThrowOnFileLength(@"C:\root\locked.txt", new UnauthorizedAccessException("Access denied."));
        var scanner = new DiskSpaceScanner(fs);
        var result = CreateNode(@"C:\root");

        await scanner.ScanDirectoryAsync(result);

        // Only accessible.txt counts: 1024 -> 1 KB
        Assert.Equal(1, result.SizeInKb);
        Assert.False(result.HasError);
    }

    [Fact]
    public async Task ScanDirectoryAsync_FileLengthThrowsFileNotFound_IgnoresFile()
    {
        var fs = new InMemoryFileSystemAccessor();
        fs.AddFile(@"C:\root", "a.txt", 2048);
        fs.AddFile(@"C:\root", "missing.txt", 100);
        fs.ThrowOnFileLength(@"C:\root\missing.txt", new FileNotFoundException("File not found."));
        var scanner = new DiskSpaceScanner(fs);
        var result = CreateNode(@"C:\root");

        await scanner.ScanDirectoryAsync(result);

        // 2048 -> 2 KB
        Assert.Equal(2, result.SizeInKb);
        Assert.False(result.HasError);
    }

    [Fact]
    public async Task ScanDirectoryAsync_PathTooLong_IgnoresFile()
    {
        var fs = new InMemoryFileSystemAccessor();
        fs.AddFile(@"C:\root", "a.txt", 512);
        fs.AddFile(@"C:\root", "long.txt", 100);
        fs.ThrowOnFileLength(@"C:\root\long.txt", new PathTooLongException("Path too long."));
        var scanner = new DiskSpaceScanner(fs);
        var result = CreateNode(@"C:\root");

        await scanner.ScanDirectoryAsync(result);

        // 512 -> 1 KB
        Assert.Equal(1, result.SizeInKb);
        Assert.False(result.HasError);
    }

    [Fact]
    public async Task ScanDirectoryAsync_UnauthorizedAccessToChildDirectory_SetsChildErrorState()
    {
        var fs = new InMemoryFileSystemAccessor();
        fs.AddFile(@"C:\root", "root.txt", 1024);
        fs.AddChildDirectory(@"C:\root", "locked");
        fs.ThrowOnEnumerateFiles(@"C:\root\locked", new UnauthorizedAccessException("Access denied."));
        var scanner = new DiskSpaceScanner(fs);
        var result = CreateNode(@"C:\root");

        await scanner.ScanDirectoryAsync(result);

        Assert.False(result.HasError);
        Assert.Single(result.Children);
        var child = result.Children[0];
        Assert.True(child.HasError);
        Assert.Equal("Access denied.", child.ErrorMessage);
        // Parent still includes child's size (0 in this case) plus its own file
        Assert.Equal(1, result.SizeInKb);
    }

    [Fact]
    public async Task ScanDirectoryAsync_HeavyChildDirectory_IsScannedAndIncludedInTotal()
    {
        var fs = new InMemoryFileSystemAccessor();
        fs.AddFile(@"C:\root", "root.txt", 1024);
        fs.AddChildDirectory(@"C:\root", "heavy");
        for (var i = 0; i < 201; i++)
        {
            fs.AddFile(@"C:\root\heavy", $"file{i}.txt", 1024);
        }
        fs.AddChildDirectory(@"C:\root", "light");
        fs.AddFile(@"C:\root\light", "file.txt", 2048);

        var scanner = new DiskSpaceScanner(fs);
        var result = CreateNode(@"C:\root");

        await scanner.ScanDirectoryAsync(result);

        // root: 1024 -> 1 KB
        // heavy: 201 * 1024 = 205824 bytes -> 201 KB
        // light: 2048 -> 2 KB
        // total: 1024 + 205824 + 2048 = 208896 bytes -> 204 KB
        Assert.Equal(204, result.SizeInKb);
        Assert.Equal(2, result.Children.Count);
        var heavy = result.Children.FirstOrDefault(c => c.Name == "heavy");
        Assert.NotNull(heavy);
        Assert.Equal(201, heavy.SizeInKb);
        var light = result.Children.FirstOrDefault(c => c.Name == "light");
        Assert.NotNull(light);
        Assert.Equal(2, light.SizeInKb);
    }

    [Fact]
    public async Task ScanDirectoryAsync_StopsListingAtDefaultLimit()
    {
        var fs = new InMemoryFileSystemAccessor();
        fs.AddDirectory(@"C:\root");
        for (var i = 0; i < DiskSpaceScanner.MaxDirectoriesToScan + 100; i++)
        {
            fs.AddChildDirectory(@"C:\root", $"dir{i}");
        }

        var scanner = new DiskSpaceScanner(fs);
        var result = CreateNode(@"C:\root");

        await scanner.ScanDirectoryAsync(result);

        Assert.Equal(DiskSpaceScanner.MaxDirectoriesToScan, scanner.DirectoriesFound);
        // The root node fills one slot of the found-directories dictionary.
        Assert.Equal(DiskSpaceScanner.MaxDirectoriesToScan - 1, result.Children.Count);
    }

    [Fact]
    public async Task ScanDirectoryAsync_ReportsDirectoryCountAndCompletionPerDirectory()
    {
        var fs = new InMemoryFileSystemAccessor();
        fs.AddFile(@"C:\root", "root.txt", 1024);
        fs.AddChildDirectory(@"C:\root", "sub1");
        fs.AddFile(@"C:\root\sub1", "a.txt", 512);
        fs.AddChildDirectory(@"C:\root", "sub2");
        fs.AddChildDirectory(@"C:\root\sub2", "deep");
        fs.AddFile(@"C:\root\sub2\deep", "b.txt", 1024);

        var scanner = new DiskSpaceScanner(fs);
        var result = CreateNode(@"C:\root");
        var completedDirs = new List<string>();
        scanner.DirectoryCompleted += (s, node) => completedDirs.Add(node.Path);
        var reports = new List<ScanStatus>();
        var progress = new Progress<ScanStatus>(reports.Add);

        await scanner.ScanDirectoryAsync(result, progress);

        // Stages: listing first, then file scanning; directory count is reported.
        Assert.Contains(reports, r => r.Stage == ScanStage.ListingDirectories && r.DirectoriesFound == 4);

        // DirectoryCompleted fires once per directory: root, sub1, sub2, deep.
        Assert.Equal(4, completedDirs.Count);
        Assert.Contains(@"C:\root", completedDirs);
        Assert.Contains(@"C:\root\sub1", completedDirs);
        Assert.Contains(@"C:\root\sub2", completedDirs);
        Assert.Contains(@"C:\root\sub2\deep", completedDirs);
    }
}

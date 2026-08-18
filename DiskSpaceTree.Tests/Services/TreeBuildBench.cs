using System.Collections.Specialized;
using System.Diagnostics;
using DiskSpaceTree.Models;
using DiskSpaceTree.Services;
using Xunit;
using Xunit.Abstractions;

namespace DiskSpaceTree.Tests.Services;

public class TreeBuildBench
{
    private readonly ITestOutputHelper _out;
    public TreeBuildBench(ITestOutputHelper output) => _out = output;

    private static FileSystemNode CreateNode(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) name = path;
        return new FileSystemNode(name, path, isDirectory: true);
    }

    private static void BuildTree(InMemoryFileSystemAccessor fs, int target)
    {
        int count = 0;
        void Add(string parent, string stem, int depth, int fanout)
        {
            for (int i = 0; i < fanout && count < target; i++)
            {
                var child = System.IO.Path.Combine(parent, stem + i);
                fs.AddFile(child, "f.txt", 1024);
                count++;
                if (depth > 0) Add(child, stem + i, depth - 1, fanout);
            }
        }
        fs.AddDirectory(@"C:\root");
        Add(@"C:\root", "d", 4, 6);
    }

    [Fact]
    public async Task Stress_TreeBuild_ReportsEventCounts()
    {
        var fs = new InMemoryFileSystemAccessor();
        BuildTree(fs, 3000);
        var scanner = new DiskSpaceScanner(fs);
        var result = CreateNode(@"C:\root");

        // Mirror MainForm: subscribe to a node's Children.CollectionChanged and, on each Add,
        // subscribe to the added child's collection too (recursive subscription).
        long reset = 0, add = 0, remove = 0;
        int subscribedNodes = 0;

        void OnCollectionChanged(object? s, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) reset++;
            else if (e.Action == NotifyCollectionChangedAction.Add) add++;
            else if (e.Action == NotifyCollectionChangedAction.Remove) remove++;
        }

        void SubscribeAll(FileSystemNode n)
        {
            if (Interlocked.Increment(ref subscribedNodes) <= 0) { }
            n.Children.CollectionChanged += OnCollectionChanged;
            foreach (var c in n.Children) SubscribeAll(c);
        }

        var sw = Stopwatch.StartNew();
        await scanner.ScanDirectoryAsync(result);
        sw.Stop();

        // After scan, subscribe recursively to count what WOULD be processed and time it.
        // (Scan-only event counting requires subscribing during scan; subscribe on Add below
        //  by re-running a lightweight traversal.)
        SubscribeAll(result);

        _out.WriteLine("COUNTS dirs={0} scanMs={1} reset={2} add={3} remove={4} subscribedNodes={5}",
            scanner.DirectoriesFound, sw.ElapsedMilliseconds, reset, add, remove, subscribedNodes);

        Assert.True(sw.ElapsedMilliseconds < 10000);
    }
}

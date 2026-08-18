using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DiskSpaceTree.Diagnostics;

namespace DiskSpaceTree.Models;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that supports suppressing change
/// notifications while a batch mutation (e.g. a size-based re-sort of children)
/// is being applied, then raising a single <see cref="NotifyCollectionChangedAction.Reset"/>.
/// This prevents flooding subscribers with one event per child.
/// </summary>
public sealed class ObservableChildCollection : ObservableCollection<FileSystemNode>
{
    private bool _suppress;

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppress)
        {
            base.OnCollectionChanged(e);
        }
    }

    public void ReplaceWith(IList<FileSystemNode> sorted)
    {
        _suppress = true;
        try
        {
            Clear();
            foreach (var child in sorted)
            {
                Add(child);
            }
        }
        finally
        {
            _suppress = false;
        }

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

public sealed class FileSystemNode : INotifyPropertyChanged
{
    private long _sizeInKb;
    private long _fileCount;
    private bool _isExpanded;
    private bool _hasError;
    private string? _errorMessage;
    public readonly object SyncRoot = new();
    const int CounterSize = 30;
    public int NotificationCounter { get; set; }

    public FileSystemNode(string name, string path, bool isDirectory)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Path = path ?? throw new ArgumentNullException(nameof(path));
        IsDirectory = isDirectory;
        Children = new ObservableChildCollection();
    }

    public string Name { get; }

    public string Path { get; }

    public bool IsDirectory { get; }

    public FileSystemNode? Parent { get; internal set; }

    public ObservableChildCollection Children { get; }

    /// <summary>Number of child directories that have not finished scanning yet.</summary>
    public int PendingChildCount;

    public long SizeInKb
    {
        get => _sizeInKb;
        set => SetProperty(ref _sizeInKb, value);
    }

    public long FileCount
    {
        get => _fileCount;
        set => SetProperty(ref _fileCount, value);
    }

    // Size of the files located directly in this directory, excluding any subdirectories.
    public long DirectSizeInKb { get; internal set; }

    public long DirectFileCount { get; internal set; }

    // Adds the given totals to this node and raises the property-change notifications
    // so the UI can refresh. Thread-safe for the concurrent scan workers.
    public void AddSizeInKb(long amount)
    {
        if (amount == 0)
        {
            return;
        }

        lock (SyncRoot)
        {
            _sizeInKb += amount;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeInKb)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplaySize)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayText)));
        }
    }

    public void AddFileCount(long count)
    {
        if (count == 0)
        {
            return;
        }

        lock (SyncRoot)
        {
            _fileCount += count;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FileCount)));
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public string DisplaySize => $"{SizeInKb:N0} KB";

    public string DirectSizeDisplay => $"{DirectSizeInKb:N0} KB";

    public string DisplayText => IsDirectory
        ? $"{Name} ({DisplaySize})"
        : $"{Name} ({DisplaySize})";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RaiseEvent()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplaySize)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayText)));
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        lock (SyncRoot)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            //NotificationCounter++;
            //if (NotificationCounter < CounterSize)
            //{
            //    return true;
            //}

            field = value;
            Logger.Log($"PropertyChanged: {Path} {propertyName} = {value}");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            // Derived/calculated display properties also need to refresh when size changes.
            if (propertyName == nameof(SizeInKb))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplaySize)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayText)));
            }

            return true;
        }
    }
}

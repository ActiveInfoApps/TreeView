using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DiskSpaceTree.Models;

public sealed class FileSystemNode : INotifyPropertyChanged
{
    private long _sizeInKb;
    private bool _isExpanded;
    private bool _hasError;
    private string? _errorMessage;

    public FileSystemNode(string name, string path, bool isDirectory)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Path = path ?? throw new ArgumentNullException(nameof(path));
        IsDirectory = isDirectory;
        Children = new ObservableCollection<FileSystemNode>();
    }

    public string Name { get; }

    public string Path { get; }

    public bool IsDirectory { get; }

    public ObservableCollection<FileSystemNode> Children { get; }

    public long SizeInKb
    {
        get => _sizeInKb;
        set => SetProperty(ref _sizeInKb, value);
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

    public string DisplayText => IsDirectory
        ? $"{Name} ({DisplaySize})"
        : $"{Name} ({DisplaySize})";

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
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

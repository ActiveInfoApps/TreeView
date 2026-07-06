using System.Windows.Input;
using DiskSpaceTree.Models;

namespace DiskSpaceTree.ViewModels;

public sealed class TreeViewItem : BindableObject
{
    private readonly FileSystemNode _node;
    private readonly TreeViewItem? _parent;
    private bool _isExpanded;

    public TreeViewItem(FileSystemNode node, int level, TreeViewItem? parent)
    {
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _parent = parent;
        Level = level;
        _isExpanded = node.IsExpanded;
        ToggleCommand = new Command(Toggle);

        // Forward property changes from the underlying node so the UI updates as sizes arrive.
        node.PropertyChanged += (sender, e) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (e.PropertyName is nameof(FileSystemNode.DisplayText) or nameof(FileSystemNode.DisplaySize))
                {
                    OnPropertyChanged(nameof(DisplayText));
                    OnPropertyChanged(nameof(DisplaySize));
                }
                else if (e.PropertyName is nameof(FileSystemNode.HasError))
                {
                    OnPropertyChanged(nameof(HasError));
                    OnPropertyChanged(nameof(ErrorMessage));
                    OnPropertyChanged(nameof(DisplayText));
                }
                else if (e.PropertyName == nameof(FileSystemNode.ErrorMessage))
                {
                    OnPropertyChanged(nameof(ErrorMessage));
                }
            });
        };
    }

    public int Level { get; }

    public FileSystemNode Node => _node;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged(nameof(IsExpanded));
            OnPropertyChanged(nameof(ExpandCollapseIcon));
        }
    }

    public string ExpandCollapseIcon => IsExpanded ? "▼" : "▶";

    public bool IsDirectory => _node.IsDirectory;

    public bool HasError => _node.HasError;

    public string? ErrorMessage => _node.ErrorMessage;

    public string DisplayText => _node.HasError ? $"{_node.DisplayText} ⚠" : _node.DisplayText;

    public string DisplaySize => _node.DisplaySize;

    public ICommand ToggleCommand { get; }

    public TreeViewItem? Parent => _parent;

    private void Toggle()
    {
        IsExpanded = !IsExpanded;
    }
}

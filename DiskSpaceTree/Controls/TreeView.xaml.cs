using System.Collections.ObjectModel;
using System.Collections.Specialized;
using DiskSpaceTree.Models;
using DiskSpaceTree.ViewModels;

namespace DiskSpaceTree.Controls;

public partial class TreeView : ContentView
{
    private readonly ObservableCollection<TreeViewItem> _visibleItems = new();
    private readonly Dictionary<ObservableCollection<FileSystemNode>, TreeViewItem> _childrenSubscriptions = new();
    private ObservableCollection<FileSystemNode>? _itemsSource;

    public TreeView()
    {
        VisibleItems = _visibleItems;
        _visibleItems.CollectionChanged += (s, e) => IsEmpty = _visibleItems.Count == 0;
        InitializeComponent();
        BindableLayout.SetItemsSource(ItemsStackLayout, _visibleItems);
    }

    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource),
        typeof(ObservableCollection<FileSystemNode>),
        typeof(TreeView),
        null,
        propertyChanged: OnItemsSourceChanged);

    public ObservableCollection<FileSystemNode>? ItemsSource
    {
        get => (ObservableCollection<FileSystemNode>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ObservableCollection<TreeViewItem> VisibleItems { get; }

    private bool _isEmpty = true;

    public bool IsEmpty
    {
        get => _isEmpty;
        private set
        {
            _isEmpty = value;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private static void OnItemsSourceChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not TreeView treeView)
        {
            return;
        }

        if (oldValue is ObservableCollection<FileSystemNode> oldCollection)
        {
            oldCollection.CollectionChanged -= treeView.OnItemsSourceCollectionChanged;
        }

        treeView._itemsSource = newValue as ObservableCollection<FileSystemNode>;

        if (treeView._itemsSource is not null)
        {
            treeView._itemsSource.CollectionChanged += treeView.OnItemsSourceCollectionChanged;
        }

        treeView.RebuildVisibleItems();
    }

    private void OnItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => RebuildVisibleItems());
    }

    private void RebuildVisibleItems()
    {
        foreach (var item in _visibleItems.ToList())
        {
            UnsubscribeFromChildren(item);
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        _visibleItems.Clear();

        if (_itemsSource is null)
        {
            return;
        }

        foreach (var node in _itemsSource)
        {
            var rootItem = new TreeViewItem(node, level: 0, parent: null);
            rootItem.PropertyChanged += OnItemPropertyChanged;
            _visibleItems.Add(rootItem);
            SubscribeToChildren(rootItem);

            if (rootItem.IsExpanded)
            {
                AddChildren(rootItem);
            }
        }
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not TreeViewItem item)
        {
            return;
        }

        if (e.PropertyName == nameof(TreeViewItem.IsExpanded))
        {
            var index = _visibleItems.IndexOf(item);
            if (index < 0)
            {
                return;
            }

            if (item.IsExpanded)
            {
                AddChildren(item, index);
            }
            else
            {
                RemoveChildren(item);
            }
        }
    }

    private void AddChildren(TreeViewItem parent, int? parentIndex = null)
    {
        var index = parentIndex ?? _visibleItems.IndexOf(parent);
        if (index < 0)
        {
            return;
        }

        var insertIndex = index + 1;
        foreach (var childNode in parent.Node.Children.ToList())
        {
            var childItem = CreateChildItem(childNode, parent);
            _visibleItems.Insert(insertIndex, childItem);
            insertIndex++;

            if (childItem.IsExpanded)
            {
                insertIndex = AddChildrenRecursive(childItem, insertIndex - 1);
            }
        }
    }

    private int AddChildrenRecursive(TreeViewItem parent, int parentIndex)
    {
        var insertIndex = parentIndex + 1;
        foreach (var childNode in parent.Node.Children.ToList())
        {
            var childItem = CreateChildItem(childNode, parent);
            _visibleItems.Insert(insertIndex, childItem);
            insertIndex++;

            if (childItem.IsExpanded)
            {
                insertIndex = AddChildrenRecursive(childItem, insertIndex - 1);
            }
        }

        return insertIndex;
    }

    private TreeViewItem CreateChildItem(FileSystemNode childNode, TreeViewItem parent)
    {
        var childItem = new TreeViewItem(childNode, parent.Level + 1, parent);
        childItem.PropertyChanged += OnItemPropertyChanged;
        SubscribeToChildren(childItem);
        return childItem;
    }

    private void SubscribeToChildren(TreeViewItem item)
    {
        if (_childrenSubscriptions.ContainsKey(item.Node.Children))
        {
            return;
        }

        item.Node.Children.CollectionChanged += OnNodeChildrenCollectionChanged;
        _childrenSubscriptions[item.Node.Children] = item;
    }

    private void UnsubscribeFromChildren(TreeViewItem item)
    {
        if (_childrenSubscriptions.Remove(item.Node.Children))
        {
            item.Node.Children.CollectionChanged -= OnNodeChildrenCollectionChanged;
        }
    }

    private void OnNodeChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => HandleNodeChildrenCollectionChanged(sender, e));
    }

    private void HandleNodeChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (sender is not ObservableCollection<FileSystemNode> children)
        {
            return;
        }

        if (!_childrenSubscriptions.TryGetValue(children, out var parent))
        {
            return;
        }

        if (!parent.IsExpanded)
        {
            return;
        }

        var parentIndex = _visibleItems.IndexOf(parent);
        if (parentIndex < 0)
        {
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            var sourceIndex = e.NewStartingIndex;
            foreach (FileSystemNode childNode in e.NewItems)
            {
                var insertIndex = sourceIndex >= 0
                    ? GetDirectChildStartIndex(parent, parentIndex, sourceIndex)
                    : GetInsertIndexAfterDescendants(parent, parentIndex);

                var childItem = CreateChildItem(childNode, parent);
                _visibleItems.Insert(insertIndex, childItem);

                if (childItem.IsExpanded)
                {
                    AddChildrenRecursive(childItem, insertIndex);
                }

                sourceIndex++;
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is not null)
        {
            foreach (FileSystemNode childNode in e.OldItems)
            {
                var childItem = _visibleItems.FirstOrDefault(i => i.Node == childNode && i.Parent == parent);
                if (childItem is not null)
                {
                    RemoveChildren(childItem);
                    _visibleItems.Remove(childItem);
                    UnsubscribeFromChildren(childItem);
                    childItem.PropertyChanged -= OnItemPropertyChanged;
                }
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            RemoveChildren(parent);
            AddChildren(parent, parentIndex);
        }
    }

    private int GetInsertIndexAfterDescendants(TreeViewItem parent, int parentIndex)
    {
        var insertIndex = parentIndex + 1;
        while (insertIndex < _visibleItems.Count && IsDescendantOf(_visibleItems[insertIndex], parent))
        {
            insertIndex++;
        }

        return insertIndex;
    }

    private int GetDirectChildStartIndex(TreeViewItem parent, int parentIndex, int childIndex)
    {
        var currentIndex = parentIndex + 1;
        var directChildrenSeen = 0;

        while (currentIndex < _visibleItems.Count && directChildrenSeen < childIndex)
        {
            var item = _visibleItems[currentIndex];
            if (item.Parent == parent)
            {
                directChildrenSeen++;
                currentIndex++;
                while (currentIndex < _visibleItems.Count && IsDescendantOf(_visibleItems[currentIndex], item))
                {
                    currentIndex++;
                }
            }
            else
            {
                currentIndex++;
            }
        }

        return currentIndex;
    }

    private void RemoveChildren(TreeViewItem parent)
    {
        var parentIndex = _visibleItems.IndexOf(parent);
        if (parentIndex < 0)
        {
            return;
        }

        var startIndex = parentIndex + 1;
        var count = 0;

        for (var i = startIndex; i < _visibleItems.Count; i++)
        {
            var current = _visibleItems[i];
            if (IsDescendantOf(current, parent))
            {
                count++;
            }
            else
            {
                break;
            }
        }

        for (var i = startIndex + count - 1; i >= startIndex; i--)
        {
            var item = _visibleItems[i];
            UnsubscribeFromChildren(item);
            item.PropertyChanged -= OnItemPropertyChanged;
            _visibleItems.RemoveAt(i);
        }
    }

    private static bool IsDescendantOf(TreeViewItem candidate, TreeViewItem ancestor)
    {
        var current = candidate.Parent;
        while (current is not null)
        {
            if (current == ancestor)
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }
}

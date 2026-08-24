namespace DiskSpaceTree.Models;

public interface IDisplayNode
{
    string DisplayText { get; }
    bool HasError { get; }
    string? ToolTipText { get; }
}

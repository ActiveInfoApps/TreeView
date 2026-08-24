using System.Windows.Forms;
using DiskSpaceTree.Models;

namespace DiskSpaceTree.WinForms;

public class DisplayTreeNode : TreeNode, IDisplayNode
{
    public string DisplayText
    {
        get => Text;
        set => Text = value;
    }

    bool IDisplayNode.HasError => ForeColor == Color.Red;

    public string? ToolTipText
    {
        get => base.ToolTipText;
        set => base.ToolTipText = value;
    }

    public DisplayTreeNode(string text, string? toolTip = null)
    {
        Text = text;
        base.ToolTipText = toolTip;
    }
}

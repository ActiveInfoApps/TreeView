using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using DiskSpaceTree.Models;
using DiskSpaceTree.Services;

namespace DiskSpaceTree.WinForms;

public partial class MainForm : Form
{
    private readonly DiskSpaceScanner _scanner;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isBusy;
    private readonly ToolStripStatusLabel _statusPathLabel;
    private readonly ToolStripStatusLabel _statusCountLabel;
    private readonly TreeView _treeView;
    private readonly ComboBox _driveComboBox;
    private readonly Button _scanButton;
    private readonly Button _cancelButton;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;

    public MainForm()
    {
        _scanner = new DiskSpaceScanner(new FileSystemAccessor());

        Text = "Disk Space Tree";
        Size = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;

        var topPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(5),
            WrapContents = false
        };

        topPanel.Controls.Add(new Label
        {
            Text = "Select a disk drive to scan:",
            AutoSize = true,
            Margin = new Padding(0, 5, 5, 0)
        });

        _driveComboBox = new ComboBox
        {
            Width = 120,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _driveComboBox.SelectedIndexChanged += DriveComboBox_SelectedIndexChanged;
        topPanel.Controls.Add(_driveComboBox);

        _scanButton = new Button
        {
            Text = "Scan",
            AutoSize = true,
            Margin = new Padding(5, 0, 0, 0)
        };
        _scanButton.Click += ScanButton_Click;
        topPanel.Controls.Add(_scanButton);

        _cancelButton = new Button
        {
            Text = "Cancel",
            AutoSize = true,
            Enabled = false,
            Margin = new Padding(5, 0, 0, 0)
        };
        _cancelButton.Click += CancelButton_Click;
        topPanel.Controls.Add(_cancelButton);

        _progressBar = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            Width = 100,
            Visible = false,
            Margin = new Padding(5, 0, 0, 0)
        };
        topPanel.Controls.Add(_progressBar);

        _statusLabel = new Label
        {
            Text = "Select a drive and click Scan.",
            AutoSize = true,
            Margin = new Padding(5, 5, 0, 0)
        };
        topPanel.Controls.Add(_statusLabel);

        _treeView = new TreeView
        {
            Dock = DockStyle.Fill,
            ShowNodeToolTips = true
        };
        _treeView.NodeMouseClick += TreeView_NodeMouseClick;
        _treeView.BeforeExpand += TreeView_BeforeExpand;

        var contextMenu = new ContextMenuStrip();
        var openMenuItem = new ToolStripMenuItem("Open in Explorer");
        openMenuItem.Click += OpenInExplorer_Click;
        contextMenu.Items.Add(openMenuItem);
        _treeView.ContextMenuStrip = contextMenu;

        var statusStrip = new StatusStrip { Dock = DockStyle.Bottom };
        _statusPathLabel = new ToolStripStatusLabel
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _statusCountLabel = new ToolStripStatusLabel { Text = "Files: 0" };
        statusStrip.Items.Add(_statusPathLabel);
        statusStrip.Items.Add(_statusCountLabel);

        Controls.Add(_treeView);
        Controls.Add(topPanel);
        Controls.Add(statusStrip);

        LoadDrives();
    }

    private void LoadDrives()
    {
        _driveComboBox.Items.Clear();
        foreach (var drive in DiskSpaceScanner.GetDrives())
        {
            _driveComboBox.Items.Add(drive);
        }

        _driveComboBox.DisplayMember = "Name";
        if (_driveComboBox.Items.Count > 0)
        {
            _driveComboBox.SelectedIndex = 0;
        }

        UpdateStatusCount();
    }

    private async void ScanButton_Click(object? sender, EventArgs e)
    {
        if (_driveComboBox.SelectedItem is not FileSystemNode selectedDrive)
        {
            return;
        }

        // Create a fresh node for each scan so previous results do not accumulate.
        var driveNode = new FileSystemNode(selectedDrive.Name, selectedDrive.Path, selectedDrive.IsDirectory);

        _cancellationTokenSource = new CancellationTokenSource();
        IsBusy = true;

        _treeView.Nodes.Clear();
        _statusPathLabel.Text = string.Empty;
        _statusCountLabel.Text = "Files: 0";
        _statusLabel.Text = "Scanning...";

        var rootTreeNode = CreateTreeNode(driveNode);
        _treeView.Nodes.Add(rootTreeNode);
        SubscribeToNode(driveNode, rootTreeNode);
        rootTreeNode.Expand();

        var progress = new Progress<ScanStatus>(status =>
        {
            _statusPathLabel.Text = status.CurrentFilePath;
            _statusCountLabel.Text = $"Files: {status.FilesProcessed:N0}";
        });

        try
        {
            await _scanner.ScanDriveAsync(driveNode, progress, _cancellationTokenSource.Token);
            _statusLabel.Text = $"Scan complete. {driveNode.DisplaySize} total.";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private void CancelButton_Click(object? sender, EventArgs e)
    {
        _cancellationTokenSource?.Cancel();
    }

    private bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            _scanButton.Enabled = !_isBusy && _driveComboBox.SelectedItem != null;
            _cancelButton.Enabled = _isBusy;
            _progressBar.Visible = _isBusy;
        }
    }

    private void DriveComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        _scanButton.Enabled = !_isBusy && _driveComboBox.SelectedItem != null;
        UpdateStatusCount();
    }

    private void UpdateStatusCount()
    {
        var count = _driveComboBox.Items.Count;
        _statusLabel.Text = count == 0
            ? "No drives available."
            : $"{count} drive(s) available. Select a drive and click Scan.";
    }

    private void TreeView_BeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        if (e.Node?.Tag is FileSystemNode node)
        {
            node.IsExpanded = true;
        }
    }

    private void TreeView_NodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            _treeView.SelectedNode = e.Node;
        }
    }

    private void OpenInExplorer_Click(object? sender, EventArgs e)
    {
        if (_treeView.SelectedNode?.Tag is FileSystemNode node)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{node.Path}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open Explorer: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private TreeNode CreateTreeNode(FileSystemNode node)
    {
        var treeNode = new TreeNode(node.DisplayText)
        {
            Tag = node,
            ToolTipText = node.Path
        };

        if (node.HasError)
        {
            treeNode.ForeColor = Color.Red;
        }

        return treeNode;
    }

    private void UpdateTreeNode(TreeNode treeNode, FileSystemNode node)
    {
        treeNode.Text = node.DisplayText;
        treeNode.ForeColor = node.HasError ? Color.Red : SystemColors.WindowText;
    }

    private void SubscribeToNode(FileSystemNode node, TreeNode treeNode)
    {
        node.PropertyChanged += (s, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(FileSystemNode.SizeInKb):
                case nameof(FileSystemNode.HasError):
                    InvokeOnUiThread(() => UpdateTreeNode(treeNode, node));
                    break;
            }
        };

        node.Children.CollectionChanged += (s, e) =>
        {
            InvokeOnUiThread(() => HandleChildrenChanged(treeNode, node, e));
        };

        foreach (var child in node.Children)
        {
            var childTreeNode = CreateTreeNode(child);
            treeNode.Nodes.Add(childTreeNode);
            SubscribeToNode(child, childTreeNode);
        }
    }

    private void HandleChildrenChanged(TreeNode parentTreeNode, FileSystemNode parentNode, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems != null)
                {
                    var index = e.NewStartingIndex >= 0 ? e.NewStartingIndex : parentTreeNode.Nodes.Count;
                    foreach (FileSystemNode child in e.NewItems)
                    {
                        var childTreeNode = CreateTreeNode(child);
                        parentTreeNode.Nodes.Insert(index, childTreeNode);
                        SubscribeToNode(child, childTreeNode);
                        index++;
                    }
                }
                break;

            case NotifyCollectionChangedAction.Reset:
                parentTreeNode.Nodes.Clear();
                foreach (var child in parentNode.Children)
                {
                    var childTreeNode = CreateTreeNode(child);
                    parentTreeNode.Nodes.Add(childTreeNode);
                    SubscribeToNode(child, childTreeNode);
                }
                break;
        }
    }

    private void InvokeOnUiThread(Action action)
    {
        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }
}

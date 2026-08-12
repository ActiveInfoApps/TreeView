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
    private readonly ToolStripStatusLabel _statusDirsLabel;
    private readonly ToolStripStatusLabel _statusTotalSizeLabel;
    private readonly TreeView _treeView;
    private readonly DataGridView _topDirectoriesGrid;
    private FileSystemNode? _rootNode;
    private readonly ComboBox _driveComboBox;
    private readonly Button _scanButton;
    private readonly Button _cancelButton;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private readonly HashSet<FileSystemNode> _dirtyNodes = [];
    private readonly Dictionary<FileSystemNode, TreeNode> _nodeMap = [];
    private readonly TabControl _tabControl;
    private readonly TabPage _treeTabPage;
    private readonly TabPage _topDirectoriesTabPage;
    private long _directoriesScannedCount;
    private string _topDirectoriesHash = string.Empty;

    public MainForm()
    {
        _scanner = new DiskSpaceScanner(new FileSystemAccessor());
        _scanner.DirectoryCompleted += Scanner_DirectoryCompleted;

        Text = "Disk Space Tree";
        Size = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;

        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 40
        };

        int x = 5;
        var label = new Label
        {
            Text = "Select a disk drive to scan:",
            AutoSize = true,
            Location = new Point(x, 10)
        };
        topPanel.Controls.Add(label);
        x += label.PreferredWidth + 10;

        _driveComboBox = new ComboBox
        {
            Width = 120,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(x, 6)
        };
        _driveComboBox.SelectedIndexChanged += DriveComboBox_SelectedIndexChanged;
        topPanel.Controls.Add(_driveComboBox);
        x += 130;

        _scanButton = new Button
        {
            Text = "Scan",
            Width = 75,
            Height = 23,
            Location = new Point(x, 6)
        };
        _scanButton.Click += ScanButton_Click;
        topPanel.Controls.Add(_scanButton);
        x += 80;

        _cancelButton = new Button
        {
            Text = "Stop",
            Width = 75,
            Height = 23,
            Enabled = false,
            Location = new Point(x, 6)
        };
        _cancelButton.Click += CancelButton_Click;
        topPanel.Controls.Add(_cancelButton);
        x += 80;

        _progressBar = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            Width = 100,
            Height = 23,
            Visible = false,
            Location = new Point(x, 6)
        };
        topPanel.Controls.Add(_progressBar);
        x += 110;

        _statusLabel = new Label
        {
            Text = "Select a drive and click Scan.",
            AutoSize = true,
            Location = new Point(x, 10)
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
        _statusDirsLabel = new ToolStripStatusLabel { Text = "Dirs: 0" };
        _statusTotalSizeLabel = new ToolStripStatusLabel { Text = "Total: 0 KB" };
        statusStrip.Items.Add(_statusPathLabel);
        statusStrip.Items.Add(_statusCountLabel);
        statusStrip.Items.Add(_statusDirsLabel);
        statusStrip.Items.Add(_statusTotalSizeLabel);

        _topDirectoriesGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        _topDirectoriesGrid.CellDoubleClick += TopDirectoriesGrid_CellDoubleClick;

        _topDirectoriesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Name",
            DataPropertyName = "Name",
            FillWeight = 30,
            ReadOnly = true
        });
        _topDirectoriesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Full Path",
            DataPropertyName = "Path",
            FillWeight = 45,
            ReadOnly = true
        });
        _topDirectoriesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Files",
            DataPropertyName = "DirectFileCount",
            FillWeight = 10,
            ReadOnly = true,
            DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        _topDirectoriesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Total Size",
            DataPropertyName = "DirectSizeDisplay",
            FillWeight = 15,
            ReadOnly = true,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight }
        });

        _treeTabPage = new TabPage("Tree View") { Dock = DockStyle.Fill };
        _treeTabPage.Controls.Add(_treeView);

        _topDirectoriesTabPage = new TabPage("Top 20 Directories") { Dock = DockStyle.Fill };

        var topHintLabel = new Label
        {
            Text = "Double-click a row to open the directory in Explorer.",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(5, 5, 5, 5),
            Font = new Font(Font.FontFamily, Font.Size, FontStyle.Bold)
        };
        _topDirectoriesTabPage.Controls.Add(_topDirectoriesGrid);
        _topDirectoriesTabPage.Controls.Add(topHintLabel);

        _tabControl = new TabControl { Dock = DockStyle.Fill };
        _tabControl.TabPages.Add(_treeTabPage);
        _tabControl.TabPages.Add(_topDirectoriesTabPage);

        Controls.Add(_tabControl);
        Controls.Add(topPanel);
        Controls.Add(statusStrip);

        _updateTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _updateTimer.Tick += UpdateTimer_Tick;
        _updateTimer.Start();

        LoadDrives();
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        _treeView.BeginUpdate();
        try
        {
            lock (_dirtyNodes)
            {
                foreach (var node in _dirtyNodes)
                {
                    if (_nodeMap.TryGetValue(node, out var treeNode))
                    {
                        UpdateTreeNode(treeNode, node);
                    }
                }

                _dirtyNodes.Clear();
            }
        }
        finally
        {
            _treeView.EndUpdate();
        }

        if (_rootNode != null)
        {
            _statusTotalSizeLabel.Text = $"Total: {_rootNode.DisplaySize}";
        }
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
        _rootNode = driveNode;
        _directoriesScannedCount = 0;
        _topDirectoriesHash = string.Empty;

        _cancellationTokenSource = new CancellationTokenSource();
        IsBusy = true;

        _treeView.Nodes.Clear();
        _topDirectoriesGrid.DataSource = null;
        _statusPathLabel.Text = string.Empty;
        _statusCountLabel.Text = "Files: 0";
        _statusLabel.Text = "Scanning...";

        var rootTreeNode = CreateTreeNode(driveNode);
        _treeView.Nodes.Add(rootTreeNode);
        SubscribeToNode(driveNode, rootTreeNode);
        rootTreeNode.Expand();

        var progress = new Progress<ScanStatus>(status =>
        {
            var folderPath = System.IO.Path.GetDirectoryName(status.CurrentFilePath) ?? status.CurrentFilePath;
            _statusPathLabel.Text = folderPath;
            _statusCountLabel.Text = $"Files: {status.FilesProcessed:N0}";

            if (status.Stage == ScanStage.ListingDirectories)
            {
                _statusDirsLabel.Text = $"Dirs found: {status.DirectoriesFound:N0}";
                _statusLabel.Text = "Scanning directories...";
            }
            else
            {
                _statusDirsLabel.Text = $"Dirs scanned: {status.DirectoriesScanned:N0} / {status.DirectoriesFound:N0}";
                _statusLabel.Text = "Scanning files...";
            }
        });

        try
        {
            // Run the scan on a thread-pool thread so the WinForms UI thread stays responsive.
            await Task.Run(() => _scanner.ScanDriveAsync(driveNode, progress, _cancellationTokenSource.Token), _cancellationTokenSource.Token);
            _statusLabel.Text = $"Scan complete. {driveNode.DisplaySize} total.";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Scan stopped.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
        }
        finally
        {
            // Even when the scan is stopped early, generate the summary from whatever was scanned so far.
            PopulateTopDirectories();
            IsBusy = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private void CancelButton_Click(object? sender, EventArgs e)
    {
        _cancellationTokenSource?.Cancel();
    }

    private void Scanner_DirectoryCompleted(object? sender, FileSystemNode node)
    {
        if (_rootNode is null)
        {
            return;
        }

        var completed = Interlocked.Increment(ref _directoriesScannedCount);
        if (completed % 100 != 0)
        {
            return;
        }

        InvokeOnUiThread(PopulateTopDirectories);
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
            OpenInExplorer(node);
        }
    }

    private void TopDirectoriesGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _topDirectoriesGrid.Rows[e.RowIndex].DataBoundItem is not FileSystemNode node)
        {
            return;
        }

        OpenInExplorer(node);
    }

    private static void OpenInExplorer(FileSystemNode node)
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

        _nodeMap[node] = treeNode;
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
                    MarkNodeDirty(node);
                    break;
            }
        };

        node.Children.CollectionChanged += (s, e) =>
        {
            InvokeOnUiThread(() => HandleChildrenChanged(treeNode, node, e));
        };

        List<FileSystemNode> children;
        lock (node.SyncRoot)
        {
            children = node.Children.ToList();
        }

        foreach (var child in children)
        {
            var childTreeNode = CreateTreeNode(child);
            treeNode.Nodes.Add(childTreeNode);
            SubscribeToNode(child, childTreeNode);
        }
    }

    private void MarkNodeDirty(FileSystemNode node)
    {
        lock (_dirtyNodes)
        {
            _dirtyNodes.Add(node);
        }
    }

    private void HandleChildrenChanged(TreeNode parentTreeNode, FileSystemNode parentNode, NotifyCollectionChangedEventArgs e)
    {
        if (_scanner.CurrentStage == ScanStage.ListingDirectories)
        {
            return;
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems != null)
                {
                    var index = e.NewStartingIndex >= 0 ? e.NewStartingIndex : parentTreeNode.Nodes.Count;
                    foreach (FileSystemNode child in e.NewItems)
                    {
                        TreeNode childTreeNode;
                        if (_nodeMap.TryGetValue(child, out var existingNode))
                        {
                            childTreeNode = existingNode;
                        }
                        else
                        {
                            childTreeNode = CreateTreeNode(child);
                            SubscribeToNode(child, childTreeNode);
                        }

                        parentTreeNode.Nodes.Insert(index, childTreeNode);
                        index++;
                    }

                    UpdateTreeNode(parentTreeNode, parentNode);
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems != null)
                {
                    foreach (FileSystemNode child in e.OldItems)
                    {
                        if (_nodeMap.TryGetValue(child, out var childTreeNode))
                        {
                            parentTreeNode.Nodes.Remove(childTreeNode);
                        }
                    }
                }
                break;

            case NotifyCollectionChangedAction.Reset:
                List<FileSystemNode> resetChildren;
                lock (parentNode.SyncRoot)
                {
                    resetChildren = parentNode.Children.ToList();
                }

                parentTreeNode.Nodes.Clear();
                foreach (var child in resetChildren)
                {
                    var childTreeNode = CreateTreeNode(child);
                    parentTreeNode.Nodes.Add(childTreeNode);
                    SubscribeToNode(child, childTreeNode);
                }

                UpdateTreeNode(parentTreeNode, parentNode);
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

    private void PopulateTopDirectories()
    {
        // Rank scanned directories by the size of their own files only, excluding subdirectories.
        var top = _scanner.GetTopDirectories(20);

        var hash = ComputeDirectoryHash(top);
        if (hash == _topDirectoriesHash)
        {
            return;
        }

        _topDirectoriesHash = hash;
        _topDirectoriesGrid.DataSource = top;
    }

    private static string ComputeDirectoryHash(IReadOnlyList<FileSystemNode> directories)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var node in directories)
        {
            builder.Append(node.Path).Append('|')
                   .Append(node.Name).Append('|')
                   .Append(node.DirectFileCount).Append('|')
                   .Append(node.DirectSizeInKb).Append(';');
        }

        using var md5 = System.Security.Cryptography.MD5.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(builder.ToString());
        var hashBytes = md5.ComputeHash(bytes);
        return Convert.ToHexString(hashBytes);
    }
}

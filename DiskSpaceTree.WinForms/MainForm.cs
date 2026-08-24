using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using DiskSpaceTree.Data.Persistence;
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
    private DateTime _scanStartedAt;
    private readonly ComboBox _driveComboBox;
    private readonly Button _scanButton;
    private readonly Button _cancelButton;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private readonly ConcurrentQueue<FileSystemNode> _treeUpdateQueue = new();
    private readonly TabControl _tabControl;
    private readonly TabPage _treeTabPage;
    private readonly TabPage _topDirectoriesTabPage;
    private readonly TabPage _sizeChangesTabPage;
    private readonly DataGridView _sizeChangesGrid;
    private readonly Label _scanNameLabel;
    private readonly ComboBox _currentExecCombo;
    private readonly ComboBox _previousExecCombo;
    private List<Data.Persistence.ExecutionDto> _executions = [];
    private long _directoriesScannedCount;
    private string _topDirectoriesHash = string.Empty;

    public MainForm()
    {
        _scanner = new DiskSpaceScanner(new FileSystemAccessor());

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
        typeof(Control).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(_treeView, true);
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
        typeof(Control).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(_topDirectoriesGrid, true);
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

        _sizeChangesGrid = new DataGridView
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
        typeof(Control).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(_sizeChangesGrid, true);
        _sizeChangesGrid.CellDoubleClick += SizeChangesGrid_CellDoubleClick;

        _sizeChangesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Name",
            DataPropertyName = "Name",
            FillWeight = 20,
            ReadOnly = true
        });
        _sizeChangesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Full Path",
            DataPropertyName = "Path",
            FillWeight = 30,
            ReadOnly = true
        });
        _sizeChangesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Prev Size",
            DataPropertyName = "PreviousSizeInKb",
            FillWeight = 10,
            ReadOnly = true,
            DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        _sizeChangesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Prev Files",
            DataPropertyName = "PreviousFileCount",
            FillWeight = 10,
            ReadOnly = true,
            DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        _sizeChangesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Current Size",
            DataPropertyName = "CurrentSizeInKb",
            FillWeight = 10,
            ReadOnly = true,
            DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        _sizeChangesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Current Files",
            DataPropertyName = "CurrentFileCount",
            FillWeight = 10,
            ReadOnly = true,
            DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        _sizeChangesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Change (KB)",
            DataPropertyName = "SizeChangeInKb",
            FillWeight = 10,
            ReadOnly = true,
            DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight }
        });

        _scanNameLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 20,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(5, 0, 0, 0),
            Font = new Font(Font.FontFamily, Font.Size, FontStyle.Bold)
        };

        _sizeChangesTabPage = new TabPage("Size Changes") { Dock = DockStyle.Fill };

        var sizeChangesTopPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 35
        };

        var currentExecLabel = new Label
        {
            Text = "Current:",
            AutoSize = true,
            Location = new Point(5, 10)
        };
        sizeChangesTopPanel.Controls.Add(currentExecLabel);

        _currentExecCombo = new ComboBox
        {
            Width = 250,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(60, 6)
        };
        _currentExecCombo.SelectedIndexChanged += ExecCombo_SelectedIndexChanged;
        sizeChangesTopPanel.Controls.Add(_currentExecCombo);

        var previousExecLabel = new Label
        {
            Text = "Compare to:",
            AutoSize = true,
            Location = new Point(330, 10)
        };
        sizeChangesTopPanel.Controls.Add(previousExecLabel);

        _previousExecCombo = new ComboBox
        {
            Width = 250,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(410, 6)
        };
        _previousExecCombo.SelectedIndexChanged += ExecCombo_SelectedIndexChanged;
        sizeChangesTopPanel.Controls.Add(_previousExecCombo);

        var reloadButton = new Button
        {
            Text = "Reload",
            Width = 75,
            Height = 23,
            Location = new Point(680, 6)
        };
        reloadButton.Click += (_, _) => RefreshSizeChangesGrid();
        sizeChangesTopPanel.Controls.Add(reloadButton);

        var showDbButton = new Button
        {
            Text = "Show Database",
            Width = 100,
            Height = 23,
            Location = new Point(760, 6)
        };
        showDbButton.Click += (_, _) =>
        {
            try
            {
                var dbDir = Path.Combine("C:", "TreeView", "Database");
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dbDir}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open Explorer: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        sizeChangesTopPanel.Controls.Add(showDbButton);

        var sizeChangesHintLabel = new Label
        {
            Text = "Double-click a row to open the directory in Explorer.",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(5, 5, 5, 5),
            Font = new Font(Font.FontFamily, Font.Size, FontStyle.Bold)
        };
        _sizeChangesTabPage.Controls.Add(_sizeChangesGrid);
        _sizeChangesTabPage.Controls.Add(_scanNameLabel);
        _sizeChangesTabPage.Controls.Add(sizeChangesHintLabel);
        _sizeChangesTabPage.Controls.Add(sizeChangesTopPanel);

        _tabControl = new TabControl { Dock = DockStyle.Fill };
        _tabControl.TabPages.Add(_treeTabPage);
        _tabControl.TabPages.Add(_topDirectoriesTabPage);
        _tabControl.TabPages.Add(_sizeChangesTabPage);

        Controls.Add(_tabControl);
        Controls.Add(topPanel);
        Controls.Add(statusStrip);

        _updateTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _updateTimer.Tick += UpdateTimer_Tick;
        _updateTimer.Start();

        LoadDrives();
        _ = LoadInitialSizeChangesAsync();
    }

    private async Task LoadInitialSizeChangesAsync()
    {
        try
        {
            var persistence = new ScanPersistenceService();
            var executions = await persistence.GetAllExecutionsAsync();
            if (executions.Count == 0)
                return;

            LoadExecutions(executions);
            if (executions.Count >= 2)
            {
                RefreshSizeChangesGrid();
            }
        }
        catch
        {
            // Non-fatal; tab stays empty.
        }
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        ProcessTreeQueue();

        if (_rootNode != null)
        {
            _statusTotalSizeLabel.Text = $"Total: {_rootNode.DisplaySize}";
        }
    }

    private void ProcessTreeQueue()
    {
        if (_treeUpdateQueue.IsEmpty)
            return;

        _treeView.BeginUpdate();
        try
        {
            while (_treeUpdateQueue.TryDequeue(out var node))
            {
                if (node.TreeNode is DisplayTreeNode existingNode)
                {
                    // Only update if the displayed text actually changed.
                    var newText = node.DisplayText;
                    if (existingNode.Text != newText)
                    {
                        existingNode.DisplayText = newText;
                    }

                    existingNode.ForeColor = node.HasError ? Color.Red : SystemColors.WindowText;
                }
                else
                {
                    // First pass: node is new — create its tree node and attach to parent.
                    var treeNode = new DisplayTreeNode(node.DisplayText, node.Path);
                    node.TreeNode = treeNode;

                    if (node.Parent?.TreeNode is DisplayTreeNode parentTreeNode)
                    {
                        parentTreeNode.Nodes.Add(treeNode);
                    }
                }
            }
        }
        finally
        {
            _treeView.EndUpdate();
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
        _sizeChangesGrid.DataSource = null;
        _currentExecCombo.DataSource = null;
        _previousExecCombo.DataSource = null;
        _statusPathLabel.Text = string.Empty;
        _statusCountLabel.Text = "Files: 0";
        _statusLabel.Text = "Scanning...";

        // Drain any stale items from a previous scan.
        while (_treeUpdateQueue.TryDequeue(out _)) { }

        // Create the root tree node and link it.
        var rootDisplayNode = new DisplayTreeNode(driveNode.DisplayText, driveNode.Path);
        driveNode.TreeNode = rootDisplayNode;
        _treeView.Nodes.Add(rootDisplayNode);
        rootDisplayNode.Expand();

        // Subscribe to scanner events — they enqueue nodes for the UI thread.
        _scanner.DirectoryDiscovered += Scanner_DirectoryDiscovered;
        _scanner.DirectoryCompleted += Scanner_DirectoryCompleted;
        _scanner.AncestorUpdated += Scanner_AncestorUpdated;

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
            _scanStartedAt = DateTime.UtcNow;
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
            _scanner.DirectoryDiscovered -= Scanner_DirectoryDiscovered;
            _scanner.DirectoryCompleted -= Scanner_DirectoryCompleted;
            _scanner.AncestorUpdated -= Scanner_AncestorUpdated;

            // Persist scan results in the background, then load executions and show changes.
            if (_rootNode is not null)
            {
                var capturedRoot = _rootNode;
                var capturedStartedAt = _scanStartedAt;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var persistence = new ScanPersistenceService();
                        await persistence.SaveScanResultsAsync(capturedRoot, capturedStartedAt);
                        var executions = await persistence.GetAllExecutionsAsync();
                        InvokeOnUiThread(() =>
                        {
                            LoadExecutions(executions);
                            RefreshSizeChangesGrid();
                        });
                    }
                    catch
                    {
                        // Persistence failures are non-fatal; do not crash the app.
                    }
                });
            }

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

    private void Scanner_DirectoryDiscovered(object? sender, FileSystemNode node)
    {
        _treeUpdateQueue.Enqueue(node);
    }

    private void Scanner_DirectoryCompleted(object? sender, FileSystemNode node)
    {
        _treeUpdateQueue.Enqueue(node);

        var completed = Interlocked.Increment(ref _directoriesScannedCount);
        if (completed % 100 != 0)
        {
            return;
        }

        InvokeOnUiThread(PopulateTopDirectories);
    }

    private void Scanner_AncestorUpdated(object? sender, FileSystemNode node)
    {
        _treeUpdateQueue.Enqueue(node);
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

    private void SizeChangesGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _sizeChangesGrid.Rows[e.RowIndex].DataBoundItem is not Data.Persistence.DirectoryChangeDto dto)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dto.Path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open Explorer: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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

    private void LoadExecutions(List<Data.Persistence.ExecutionDto> executions)
    {
        _executions = executions;

        // Detach the event handler while setting up to avoid premature refreshes.
        _currentExecCombo.SelectedIndexChanged -= ExecCombo_SelectedIndexChanged;
        _previousExecCombo.SelectedIndexChanged -= ExecCombo_SelectedIndexChanged;

        // Give each combo its own list copy so they don't interfere with each other.
        _currentExecCombo.DataSource = null;
        _currentExecCombo.DataSource = new List<Data.Persistence.ExecutionDto>(_executions);
        _currentExecCombo.DisplayMember = "DisplayText";
        _currentExecCombo.ValueMember = "Id";

        _previousExecCombo.DataSource = null;
        _previousExecCombo.DataSource = new List<Data.Persistence.ExecutionDto>(_executions);
        _previousExecCombo.DisplayMember = "DisplayText";
        _previousExecCombo.ValueMember = "Id";

        if (_executions.Count >= 1)
            _currentExecCombo.SelectedIndex = 0;
        if (_executions.Count >= 2)
            _previousExecCombo.SelectedIndex = 1;

        // Reattach after setup is complete.
        _currentExecCombo.SelectedIndexChanged += ExecCombo_SelectedIndexChanged;
        _previousExecCombo.SelectedIndexChanged += ExecCombo_SelectedIndexChanged;
    }

    private void ExecCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        RefreshSizeChangesGrid();
    }

    private async void RefreshSizeChangesGrid()
    {
        if (_currentExecCombo.SelectedItem is not Data.Persistence.ExecutionDto current)
        {
            return;
        }

        if (_previousExecCombo.SelectedItem is Data.Persistence.ExecutionDto previous &&
            current.Id != previous.Id)
        {
            _scanNameLabel.Text = $"Changes: {current.DisplayText} vs {previous.DisplayText}";
            await LoadChangesAsync(current.Id, previous.Id);
        }
        else
        {
            _scanNameLabel.Text = $"Top directories: {current.DisplayText}";
            await LoadTopDirectoriesAsync(current.Id);
        }
    }

    private async Task LoadChangesAsync(Guid currentId, Guid previousId)
    {
        try
        {
            var persistence = new ScanPersistenceService();
            var changes = await persistence.GetChangedDirectoriesAsync(currentId, previousId, 50);
            InvokeOnUiThread(() =>
            {
                _sizeChangesGrid.DataSource = changes;
                if (changes.Count > 0)
                {
                    _tabControl.SelectedTab = _sizeChangesTabPage;
                }
            });
        }
        catch
        {
            // Non-fatal; grid stays empty.
        }
    }

    private async Task LoadTopDirectoriesAsync(Guid executionId)
    {
        try
        {
            var persistence = new ScanPersistenceService();
            var dirs = await persistence.GetTopDirectoriesAsync(executionId, 20);
            InvokeOnUiThread(() =>
            {
                _sizeChangesGrid.DataSource = dirs;
                if (dirs.Count > 0)
                {
                    _tabControl.SelectedTab = _sizeChangesTabPage;
                }
            });
        }
        catch
        {
            // Non-fatal; grid stays empty.
        }
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

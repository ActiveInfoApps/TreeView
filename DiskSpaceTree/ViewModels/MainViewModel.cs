using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DiskSpaceTree.Models;
using DiskSpaceTree.Services;

namespace DiskSpaceTree.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly DiskSpaceScanner _scanner = new(new FileSystemAccessor());
    private CancellationTokenSource _cancellationTokenSource = new();
    private ObservableCollection<FileSystemNode> _drives = new();
    private ObservableCollection<FileSystemNode> _rootNodes = new();
    private FileSystemNode? _selectedDrive;
    private bool _isBusy;
    private string _statusMessage = "Ready";
    private bool _hasScanned;
    private string _currentFilePath = string.Empty;
    private long _filesProcessed;

    public MainViewModel()
    {
        ScanSelectedDriveCommand = new Command(async () => await ScanSelectedDriveAsync(), () => !IsBusy && _selectedDrive is not null);
        LoadDrivesCommand = new Command(LoadDrives, () => !IsBusy);
        CancelCommand = new Command(CancelScan, () => IsBusy);

        LoadDrives();
    }

    public ObservableCollection<FileSystemNode> Drives
    {
        get => _drives;
        private set => SetProperty(ref _drives, value);
    }

    public FileSystemNode? SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            if (SetProperty(ref _selectedDrive, value))
            {
                (ScanSelectedDriveCommand as Command)?.ChangeCanExecute();
                HasScanned = false;
                RootNodes = new ObservableCollection<FileSystemNode>();
            }
        }
    }

    public ObservableCollection<FileSystemNode> RootNodes
    {
        get => _rootNodes;
        private set => SetProperty(ref _rootNodes, value);
    }

    public bool HasScanned
    {
        get => _hasScanned;
        private set => SetProperty(ref _hasScanned, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                (ScanSelectedDriveCommand as Command)?.ChangeCanExecute();
                (LoadDrivesCommand as Command)?.ChangeCanExecute();
                (CancelCommand as Command)?.ChangeCanExecute();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string CurrentFilePath
    {
        get => _currentFilePath;
        private set => SetProperty(ref _currentFilePath, value);
    }

    public long FilesProcessed
    {
        get => _filesProcessed;
        private set => SetProperty(ref _filesProcessed, value);
    }

    public ICommand ScanSelectedDriveCommand { get; }

    public ICommand LoadDrivesCommand { get; }

    public ICommand CancelCommand { get; }

    private void LoadDrives()
    {
        var drives = new ObservableCollection<FileSystemNode>(DiskSpaceScanner.GetDrives());
        Drives = drives;
        SelectedDrive = drives.FirstOrDefault();
        HasScanned = false;
        RootNodes = new ObservableCollection<FileSystemNode>();
        StatusMessage = $"{drives.Count} drive(s) available. Select a drive and tap Scan.";
    }

    private void CancelScan()
    {
        if (!IsBusy)
        {
            return;
        }

        try
        {
            _cancellationTokenSource.Cancel();
            StatusMessage = "Cancelling scan...";
        }
        catch (ObjectDisposedException)
        {
            // Token source was already disposed; ignore.
        }
    }

    private async Task ScanSelectedDriveAsync()
    {
        if (IsBusy || _selectedDrive is null)
        {
            return;
        }

        CancellationTokenSource scanTokenSource;
        try
        {
            _cancellationTokenSource.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed.
        }
        finally
        {
            scanTokenSource = new CancellationTokenSource();
            _cancellationTokenSource = scanTokenSource;
        }

        var progress = new Progress<ScanStatus>(status =>
        {
            CurrentFilePath = status.CurrentFilePath;
            FilesProcessed = status.FilesProcessed;
        });

        try
        {
            IsBusy = true;
            HasScanned = true;
            CurrentFilePath = string.Empty;
            FilesProcessed = 0;
            StatusMessage = $"Scanning {_selectedDrive.Name}...";

            var driveNode = new FileSystemNode(_selectedDrive.Name, _selectedDrive.Path, isDirectory: true)
            {
                IsExpanded = true
            };
            RootNodes = new ObservableCollection<FileSystemNode> { driveNode };

            await Task.Run(async () => await _scanner.ScanDriveAsync(driveNode, progress, scanTokenSource.Token), scanTokenSource.Token);

            StatusMessage = $"Scan complete: {_selectedDrive.Name} ({driveNode.DisplaySize}).";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            CurrentFilePath = string.Empty;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

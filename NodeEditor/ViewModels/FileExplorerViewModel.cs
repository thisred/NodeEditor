using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NodeEditor.ViewModels;

/// <summary>
/// 文件列表中单个图文件项
/// </summary>
public class FileItemViewModel : ViewModelBase
{
    public string FileName { get; }
    public string FilePath { get; }

    private bool _isActive;

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    private bool _isDirty;

    public bool IsDirty
    {
        get => _isDirty;
        set => Set(ref _isDirty, value);
    }

    public FileItemViewModel(string filePath)
    {
        FilePath = filePath;
        FileName = Path.GetFileNameWithoutExtension(filePath);
    }
}

/// <summary>
/// 文件浏览器视图模型 — 管理工作区文件夹中的技能图文件列表
/// </summary>
public class FileExplorerViewModel : ViewModelBase
{
    private string? _workspacePath;

    public string? WorkspacePath
    {
        get => _workspacePath;
        private set
        {
            Set(ref _workspacePath, value);
            OnPropertyChanged(nameof(HasWorkspace));
        }
    }

    public bool HasWorkspace => !string.IsNullOrEmpty(_workspacePath);

    public ObservableCollection<FileItemViewModel> Files { get; } = new();

    private FileItemViewModel? _activeFile;

    public FileItemViewModel? ActiveFile
    {
        get => _activeFile;
        set
        {
            if (_activeFile != null) _activeFile.IsActive = false;
            Set(ref _activeFile, value);
            if (_activeFile != null) _activeFile.IsActive = true;
            ActiveFileChanged?.Invoke(_activeFile?.FilePath);
        }
    }

    private bool _isCollapsed;

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set => Set(ref _isCollapsed, value);
    }

    public ICommand OpenFolderCommand { get; }
    public ICommand NewGraphCommand { get; }
    public ICommand SelectFileCommand { get; }
    public ICommand ToggleSidebarCommand { get; }

    public Func<Task<string?>>? RequestFolderSelection { get; set; }
    public Func<Task<string?>>? RequestNewFileName { get; set; }
    public Func<string, Task<bool>>? RequestOverwriteConfirmation { get; set; }

    public event Action<string?>? ActiveFileChanged;
    public event Action? FilesChanged;
    public event Action<string>? NewGraphCreated;

    public FileExplorerViewModel()
    {
        OpenFolderCommand = new RelayCommand(_ => OpenFolder());
        NewGraphCommand = new RelayCommand(_ => CreateNewGraph());
        SelectFileCommand = new RelayCommand(param => SelectFile(param as FileItemViewModel));
        ToggleSidebarCommand = new RelayCommand(_ => IsCollapsed = !IsCollapsed);
    }

    public async void OpenFolder()
    {
        try
        {
            var path = RequestFolderSelection != null ? await RequestFolderSelection() : null;
            if (string.IsNullOrEmpty(path)) return;
            SetWorkspace(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenFolder] 异常: {ex.Message}");
        }
    }

    public void SetWorkspace(string path)
    {
        WorkspacePath = path;
        RefreshFiles();
    }

    public void RefreshFiles()
    {
        if (string.IsNullOrEmpty(_workspacePath)) return;

        var activePath = _activeFile?.FilePath;
        Files.Clear();

        if (!Directory.Exists(_workspacePath)) return;

        foreach (var file in Directory.GetFiles(_workspacePath, "*.json")
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var item = new FileItemViewModel(file);
            if (file == activePath)
            {
                item.IsActive = true;
                _activeFile = item;
            }

            Files.Add(item);
        }

        FilesChanged?.Invoke();
    }

    public void SelectFile(FileItemViewModel? file)
    {
        if (file == null) return;
        ActiveFile = file;
    }

    public void SetActiveFile(string? filePath)
    {
        if (filePath == null)
        {
            ActiveFile = null;
            return;
        }

        var item = Files.FirstOrDefault(f => f.FilePath == filePath);
        if (item == null)
        {
            RefreshFiles();
            item = Files.FirstOrDefault(f => f.FilePath == filePath);
        }

        ActiveFile = item;
    }

    public async void CreateNewGraph()
    {
        try
        {
            if (string.IsNullOrEmpty(_workspacePath))
            {
                OpenFolder();
                return; // OpenFolder is now async, it will handle the rest
            }

            var name = RequestNewFileName != null ? await RequestNewFileName() : null;
            if (string.IsNullOrEmpty(name)) return;

            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                name += ".json";

            var path = Path.Combine(_workspacePath, name);

            if (File.Exists(path))
            {
                var confirm = RequestOverwriteConfirmation != null
                    ? await RequestOverwriteConfirmation(path)
                    : false;
                if (!confirm)
                {
                    SetActiveFile(path);
                    return;
                }
            }

            NewGraphCreated?.Invoke(path);
            RefreshFiles();
            SetActiveFile(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CreateNewGraph] 异常: {ex.Message}");
        }
    }

    public void MarkActiveFileDirty()
    {
        if (_activeFile != null) _activeFile.IsDirty = true;
    }

    public void MarkActiveFileClean()
    {
        if (_activeFile != null) _activeFile.IsDirty = false;
    }
}
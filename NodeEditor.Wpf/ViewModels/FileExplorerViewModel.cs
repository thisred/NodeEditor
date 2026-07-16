using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace NodeEditor.Wpf.ViewModels;

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
    /// <summary>显示未保存标记（●）</summary>
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
    /// <summary>工作区文件夹路径（null = 未打开）</summary>
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

    /// <summary>文件夹中的 .json 图文件列表</summary>
    public ObservableCollection<FileItemViewModel> Files { get; } = new();

    /// <summary>当前活跃的文件（null = 未选中任何文件）</summary>
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

    /// <summary>侧栏是否折叠</summary>
    private bool _isCollapsed;
    public bool IsCollapsed
    {
        get => _isCollapsed;
        set => Set(ref _isCollapsed, value);
    }

    // ── 命令 ──
    public ICommand OpenFolderCommand { get; }
    public ICommand NewGraphCommand { get; }
    public ICommand SelectFileCommand { get; }
    public ICommand ToggleSidebarCommand { get; }

    // ── 事件 ──
    /// <summary>请求选择文件夹（View 层处理）</summary>
    public Func<string?>? RequestFolderSelection { get; set; }

    /// <summary>请求输入新文件名（View 层处理）</summary>
    public Func<string?>? RequestNewFileName { get; set; }

    /// <summary>请求确认覆盖已存在文件（View 层处理，参数为文件路径，返回 true=确认覆盖）</summary>
    public Func<string, bool>? RequestOverwriteConfirmation { get; set; }

    /// <summary>活跃文件变更事件（参数为文件路径，null = 新建空图）</summary>
    public event Action<string?>? ActiveFileChanged;

    /// <summary>文件列表变更事件（文件增删后触发）</summary>
    public event Action? FilesChanged;

    /// <summary>新图文件创建事件（参数为新文件路径，EditorViewModel 负责保存空图并加载）</summary>
    public event Action<string>? NewGraphCreated;

    public FileExplorerViewModel()
    {
        OpenFolderCommand = new RelayCommand(_ => OpenFolder());
        NewGraphCommand = new RelayCommand(_ => CreateNewGraph());
        SelectFileCommand = new RelayCommand(param => SelectFile(param as FileItemViewModel));
        ToggleSidebarCommand = new RelayCommand(_ => IsCollapsed = !IsCollapsed);
    }

    /// <summary>打开文件夹，扫描 .json 文件</summary>
    public void OpenFolder()
    {
        var path = RequestFolderSelection?.Invoke();
        if (string.IsNullOrEmpty(path)) return;
        SetWorkspace(path);
    }

    /// <summary>设置工作区路径并扫描文件</summary>
    public void SetWorkspace(string path)
    {
        WorkspacePath = path;
        RefreshFiles();
    }

    /// <summary>刷新文件列表（扫描工作区 .json 文件）</summary>
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

    /// <summary>选中文件（切换活跃文件）</summary>
    public void SelectFile(FileItemViewModel? file)
    {
        if (file == null) return;
        ActiveFile = file;
    }

    /// <summary>通过路径设置活跃文件（加载完成后调用）</summary>
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
            // 文件可能刚创建，不在列表中，刷新一下
            RefreshFiles();
            item = Files.FirstOrDefault(f => f.FilePath == filePath);
        }
        ActiveFile = item;
    }

    /// <summary>创建新图文件</summary>
    public void CreateNewGraph()
    {
        if (string.IsNullOrEmpty(_workspacePath))
        {
            // 没有工作区时先要求打开文件夹
            OpenFolder();
            if (string.IsNullOrEmpty(_workspacePath)) return;
        }

        var name = RequestNewFileName?.Invoke();
        if (string.IsNullOrEmpty(name)) return;

        // 确保有 .json 扩展名
        if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            name += ".json";

        var path = Path.Combine(_workspacePath, name);

        // 文件已存在时确认是否覆盖
        if (File.Exists(path))
        {
            var confirm = RequestOverwriteConfirmation?.Invoke(path) ?? false;
            if (!confirm)
            {
                // 用户取消，直接切换到该已有文件
                SetActiveFile(path);
                return;
            }
        }

        // 立即创建空图文件到磁盘
        NewGraphCreated?.Invoke(path);

        RefreshFiles();
        SetActiveFile(path);
    }

    /// <summary>标记当前文件为已修改（未保存）</summary>
    public void MarkActiveFileDirty()
    {
        if (_activeFile != null)
            _activeFile.IsDirty = true;
    }

    /// <summary>标记当前文件为已保存</summary>
    public void MarkActiveFileClean()
    {
        if (_activeFile != null)
            _activeFile.IsDirty = false;
    }
}

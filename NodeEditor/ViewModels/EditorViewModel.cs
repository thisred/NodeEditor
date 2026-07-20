using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using NodeEditor.Core.Discovery;
using NodeEditor.Core.Models;
using NodeEditor.Serialization;

namespace NodeEditor.ViewModels;

/// <summary>
/// 编辑器主视图模型 — 管理节点图状态、节点发现、序列化，以及所有编辑器命令。
/// </summary>
public class EditorViewModel : ViewModelBase
{
    public NodeGraph Graph { get; }
    public NodeDiscovery Discovery { get; }
    public JsonGraphSerializer Serializer { get; }
    public FileExplorerViewModel FileExplorer { get; }

    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();
    public ObservableCollection<NodeCategoryViewModel> AvailableNodes { get; } = new();

    // ── 选中状态 ──
    private NodeViewModel? _selectedNode;

    public NodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            Set(ref _selectedNode, value);
            OnPropertyChanged(nameof(HasSelectedNode));
        }
    }

    public bool HasSelectedNode => SelectedNode != null;

    public List<NodeViewModel> GetSelectedNodes() => Nodes.Where(n => n.IsSelected).ToList();

    public void SelectNode(NodeViewModel node)
    {
        DeselectAll();
        node.IsSelected = true;
        SelectedNode = node;
    }

    public void ToggleNodeSelection(NodeViewModel node)
    {
        node.IsSelected = !node.IsSelected;
        if (node.IsSelected)
            SelectedNode = node;
        else if (SelectedNode == node)
            SelectedNode = GetSelectedNodes().LastOrDefault();
    }

    public void SelectNodesInRect(double x1, double y1, double x2, double y2)
    {
        DeselectAll();
        AddNodesInRectToSelection(x1, y1, x2, y2);
    }

    public void AddNodesInRectToSelection(double x1, double y1, double x2, double y2)
    {
        var minX = Math.Min(x1, x2);
        var maxX = Math.Max(x1, x2);
        var minY = Math.Min(y1, y2);
        var maxY = Math.Max(y1, y2);
        const double nodeW = NodeViewModel.ApproxWidth;
        const double nodeH = NodeViewModel.ApproxHeight;

        NodeViewModel? last = null;
        foreach (var n in Nodes)
        {
            if (n.X + nodeW >= minX && n.X <= maxX && n.Y + nodeH >= minY && n.Y <= maxY)
            {
                n.IsSelected = true;
                last = n;
            }
        }

        if (last != null) SelectedNode = last;
    }

    // ── 画布变换 ──
    private double _zoom = 1.0;

    public double Zoom
    {
        get => _zoom;
        set => Set(ref _zoom, Math.Clamp(value, 0.2, 3.0));
    }

    private double _panX;

    public double PanX
    {
        get => _panX;
        set => Set(ref _panX, value);
    }

    private double _panY;

    public double PanY
    {
        get => _panY;
        set => Set(ref _panY, value);
    }

    // ── 临时连线 ──
    private PortViewModel? _pendingSourcePort;

    public PortViewModel? PendingSourcePort
    {
        get => _pendingSourcePort;
        set
        {
            Set(ref _pendingSourcePort, value);
            OnPropertyChanged(nameof(HasPendingConnection));
        }
    }

    public bool HasPendingConnection => PendingSourcePort != null;

    private double _pendingX;

    public double PendingX
    {
        get => _pendingX;
        set => Set(ref _pendingX, value);
    }

    private double _pendingY;

    public double PendingY
    {
        get => _pendingY;
        set => Set(ref _pendingY, value);
    }

    public string? PendingPathData
    {
        get
        {
            if (PendingSourcePort == null) return null;
            var x1 = PendingSourcePort.CenterX;
            var y1 = PendingSourcePort.CenterY;
            var midX = (x1 + PendingX) / 2.0;
            return $"M {x1},{y1} C {midX},{y1} {midX},{PendingY} {PendingX},{PendingY}";
        }
    }

    public void UpdatePendingPath() => OnPropertyChanged(nameof(PendingPathData));

    // ── 命令 ──
    public ICommand CreateNodeCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand DeleteNodeCommand { get; }
    public ICommand DeleteConnectionCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ExecuteCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand SaveCommand { get; }

    // ── 文件管理 ──
    public string? CurrentFilePath { get; private set; }
    private bool _isLoading;

    private bool _isDirty;

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (Set(ref _isDirty, value))
                OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public string WindowTitle
    {
        get
        {
            var name = string.IsNullOrEmpty(CurrentFilePath)
                ? "未命名"
                : System.IO.Path.GetFileName(CurrentFilePath);
            return IsDirty ? $"● {name} — Node Editor" : $"{name} — Node Editor";
        }
    }

    private void MarkDirty()
    {
        if (_isLoading) return;
        IsDirty = true;
        FileExplorer.MarkActiveFileDirty();
    }

    public void LoadFile(string? filePath)
    {
        _isLoading = true;
        try
        {
            ClearGraph();
            if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
            {
                var newGraph = Serializer.DeserializeFromFile(filePath);
                Graph.ImportFrom(newGraph);
                CurrentFilePath = filePath;
                GraphImported?.Invoke();
            }
            else
            {
                CurrentFilePath = filePath;
            }
        }
        finally
        {
            _isLoading = false;
        }

        FileExplorer.SetActiveFile(filePath);
        FileExplorer.MarkActiveFileClean();
        IsDirty = false;
        OnPropertyChanged(nameof(WindowTitle));
    }

    public bool SaveCurrent()
    {
        if (string.IsNullOrEmpty(CurrentFilePath)) return false;
        if (!IsDirty) return true; // 无改动，跳过磁盘写入（避免自动保存每次都写盘）
        Serializer.SerializeToFile(Graph, CurrentFilePath);
        IsDirty = false;
        FileExplorer.MarkActiveFileClean();
        return true;
    }

    public string CurrentFileName =>
        string.IsNullOrEmpty(CurrentFilePath) ? "未命名" : System.IO.Path.GetFileName(CurrentFilePath);

    // ── 回调/事件 ──
    public Func<Task<string?>>? RequestSavePath { get; set; }
    public Func<Task<string?>>? RequestOpenPath { get; set; }
    public event Action? NodePositionsChanged;
    public event Action? GraphImported;

    private int _nodeCounter;

    public EditorViewModel()
    {
        Graph = new NodeGraph();
        Discovery = new NodeDiscovery();
        Serializer = new JsonGraphSerializer();

        Discovery.ScanAssembly(typeof(NodeEditor.Samples.MathNode).Assembly);
        RefreshAvailableNodes();

        Graph.NodeAdded += OnNodeAdded;
        Graph.NodeRemoved += OnNodeRemoved;
        Graph.ConnectionAdded += OnConnectionAdded;
        Graph.ConnectionRemoved += OnConnectionRemoved;

        FileExplorer = new FileExplorerViewModel();
        FileExplorer.ActiveFileChanged += OnActiveFileChanged;
        FileExplorer.NewGraphCreated += OnNewGraphCreated;

        CreateNodeCommand = new RelayCommand(param => CreateNode(param as string));
        DeleteSelectedCommand = new RelayCommand(() => DeleteSelectedNode());
        DeleteNodeCommand = new RelayCommand(param => DeleteNode(param as NodeViewModel));
        DeleteConnectionCommand = new RelayCommand(param => DeleteConnection(param as ConnectionViewModel));
        ExportCommand = new RelayCommand(() => Export());
        ImportCommand = new RelayCommand(() => Import());
        ExecuteCommand = new RelayCommand(() => ExecuteGraph());
        ClearCommand = new RelayCommand(() => ClearGraph());
        SaveCommand = new RelayCommand(() => SaveCurrent());
    }

    private void RefreshAvailableNodes()
    {
        AvailableNodes.Clear();
        foreach (var (category, descriptors) in Discovery.GetByCategory())
            AvailableNodes.Add(new NodeCategoryViewModel(category, descriptors));
    }

    public void CreateNode(string? typeId, double x = 200, double y = 150)
    {
        if (string.IsNullOrEmpty(typeId)) return;
        var node = Discovery.CreateNode(typeId);
        if (node == null) return;
        _nodeCounter++;
        node.X = x + _nodeCounter * 30;
        node.Y = y + _nodeCounter * 30;
        Graph.AddNode(node);
    }

    private void OnNodeAdded(NodeBase node)
    {
        var nvm = new NodeViewModel(node);
        nvm.PositionChanged += OnNodePositionChanged;
        Nodes.Add(nvm);
        MarkDirty();
    }

    private void OnNodeRemoved(NodeBase node)
    {
        var nvm = Nodes.FirstOrDefault(n => n.Id == node.Id);
        if (nvm != null)
        {
            nvm.PositionChanged -= OnNodePositionChanged;
            Nodes.Remove(nvm);
            if (SelectedNode == nvm)
                SelectedNode = GetSelectedNodes().LastOrDefault();
        }

        MarkDirty();
    }

    public void DeleteNode(NodeViewModel? nodeVm)
    {
        if (nodeVm == null) return;
        Graph.RemoveNode(nodeVm.Node);
    }

    public void DeleteSelectedNode()
    {
        var selected = GetSelectedNodes();
        if (selected.Count == 0 && SelectedNode != null)
            selected.Add(SelectedNode);
        foreach (var nvm in selected)
            Graph.RemoveNode(nvm.Node);
    }

    private void OnNodePositionChanged()
    {
        NodePositionsChanged?.Invoke();
        MarkDirty();
    }

    // ── 连线管理 ──

    public bool TryConnect(PortViewModel source, PortViewModel target)
    {
        if (source.IsInput && target.IsOutput)
            (source, target) = (target, source);

        if (!source.IsOutput || !target.IsInput) return false;
        var conn = Graph.TryConnect(source.NodeId, source.PortId, target.NodeId, target.PortId);
        return conn != null;
    }

    private void OnConnectionAdded(NodeConnection conn)
    {
        var sourceNodeVm = Nodes.FirstOrDefault(n => n.Id == conn.SourceNodeId);
        var targetNodeVm = Nodes.FirstOrDefault(n => n.Id == conn.TargetNodeId);
        if (sourceNodeVm == null || targetNodeVm == null) return;

        var sourcePortVm = sourceNodeVm.GetPortViewModel(conn.SourcePortId);
        var targetPortVm = targetNodeVm.GetPortViewModel(conn.TargetPortId);
        if (sourcePortVm == null || targetPortVm == null) return;

        var cvm = new ConnectionViewModel(conn, sourcePortVm, targetPortVm);
        Connections.Add(cvm);

        sourcePortVm.RefreshConnectionState();
        targetPortVm.RefreshConnectionState();
        MarkDirty();
    }

    private void OnConnectionRemoved(NodeConnection conn)
    {
        var cvm = Connections.FirstOrDefault(c => c.Connection.Id == conn.Id);
        if (cvm != null)
        {
            cvm.Detach();
            Connections.Remove(cvm);

            var sourceNodeVm = Nodes.FirstOrDefault(n => n.Id == conn.SourceNodeId);
            var targetNodeVm = Nodes.FirstOrDefault(n => n.Id == conn.TargetNodeId);
            sourceNodeVm?.GetPortViewModel(conn.SourcePortId)?.RefreshConnectionState();
            targetNodeVm?.GetPortViewModel(conn.TargetPortId)?.RefreshConnectionState();
        }

        MarkDirty();
    }

    public void DeleteConnection(ConnectionViewModel? connVm)
    {
        if (connVm == null) return;
        Graph.RemoveConnection(connVm.Connection);
    }

    // ── 临时连线 ──

    public void StartPendingConnection(PortViewModel port)
    {
        PendingSourcePort = port;
        PendingX = port.CenterX;
        PendingY = port.CenterY;
    }

    public void UpdatePendingConnection(double x, double y)
    {
        PendingX = x;
        PendingY = y;
        UpdatePendingPath();
    }

    public void CancelPendingConnection() => PendingSourcePort = null;

    public bool CompletePendingConnection(PortViewModel? targetPort)
    {
        if (PendingSourcePort == null || targetPort == null)
        {
            CancelPendingConnection();
            return false;
        }

        var success = TryConnect(PendingSourcePort, targetPort);
        CancelPendingConnection();
        return success;
    }

    // ── 导入/导出 ──

    public async void Export()
    {
        try
        {
            var path = RequestSavePath != null ? await RequestSavePath() : null;
            if (string.IsNullOrEmpty(path)) return;
            Serializer.SerializeToFile(Graph, path);
        }
        catch (Exception ex)
        {
            ExecutionLogger.Log($"导出失败：{ex.Message}");
        }
    }

    public async void Import()
    {
        try
        {
            var path = RequestOpenPath != null ? await RequestOpenPath() : null;
            if (string.IsNullOrEmpty(path)) return;

            var newGraph = Serializer.DeserializeFromFile(path);
            ClearGraph();
            Graph.ImportFrom(newGraph);
            GraphImported?.Invoke();
        }
        catch (Exception ex)
        {
            ExecutionLogger.Log($"导入失败：{ex.Message}");
        }
    }

    public async void ExecuteGraph()
    {
        ExecutionLogger.Clear();
        var nodeCount = Graph.Nodes.Count;
        ExecutionLogger.Log($"开始执行 — {nodeCount} 个节点");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await Task.Run(() => Graph.Execute());
        }
        catch (Exception ex)
        {
            ExecutionLogger.Log($"执行异常：{ex.Message}");
        }

        sw.Stop();
        ExecutionLogger.Log($"执行完成 — 耗时 {sw.ElapsedMilliseconds}ms");
    }

    public void ClearGraph()
    {
        Graph.Clear();
        if (!_isLoading) MarkDirty();
    }

    private void OnActiveFileChanged(string? filePath)
    {
        if (filePath == CurrentFilePath) return;
        if (IsDirty) SaveCurrent();
        LoadFile(filePath);
    }

    private void OnNewGraphCreated(string filePath)
    {
        if (IsDirty) SaveCurrent();

        _isLoading = true;
        try
        {
            ClearGraph();
            CurrentFilePath = filePath;
            Serializer.SerializeToFile(Graph, filePath);
        }
        finally
        {
            _isLoading = false;
        }

        IsDirty = false;
        FileExplorer.MarkActiveFileClean();
        OnPropertyChanged(nameof(WindowTitle));
        GraphImported?.Invoke();
    }

    public void DeselectAll()
    {
        foreach (var n in Nodes) n.IsSelected = false;
        SelectedNode = null;
    }

    // ── 复制/粘贴 ──

    public async void CopySelectedNodes()
    {
        try
        {
            var selected = GetSelectedNodes();
            if (selected.Count == 0) return;

            var nodeIds = new HashSet<string>(selected.Select(n => n.Id));
            var json = Serializer.SerializeNodes(Graph, nodeIds);
            await Clipboard.SetTextAsync(json);
        }
        catch (Exception ex)
        {
            ExecutionLogger.Log($"复制失败：{ex.Message}");
        }
    }

    public async void PasteNodes(double offsetX, double offsetY)
    {
        try
        {
            var json = await Clipboard.GetTextAsync();
            if (string.IsNullOrEmpty(json)) return;

            var pastedGraph = Serializer.DeserializeWithNewIds(json, offsetX, offsetY);
            DeselectAll();
            Graph.ImportFrom(pastedGraph);

            foreach (var node in pastedGraph.Nodes)
            {
                var nvm = Nodes.FirstOrDefault(n => n.Id == node.Id);
                if (nvm != null)
                {
                    nvm.IsSelected = true;
                    SelectedNode = nvm;
                }
            }
        }
        catch
        {
            /* 剪贴板内容无效或读取失败 */
        }
    }
}

/// <summary>节点面板中的一个分类</summary>
public class NodeCategoryViewModel : ViewModelBase
{
    public string CategoryName { get; }
    public ObservableCollection<NodeDescriptor> Nodes { get; }

    public NodeCategoryViewModel(string categoryName, List<NodeDescriptor> nodes)
    {
        CategoryName = categoryName;
        Nodes = new ObservableCollection<NodeDescriptor>(nodes);
    }
}
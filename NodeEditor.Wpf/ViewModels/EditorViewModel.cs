using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using NodeEditor.Core.Discovery;
using NodeEditor.Core.Models;
using NodeEditor.Serialization;

namespace NodeEditor.Wpf.ViewModels;

/// <summary>
/// 编辑器主视图模型 — 管理节点图状态、节点发现、序列化，以及所有编辑器命令。
/// </summary>
public class EditorViewModel : ViewModelBase
{
    // ── 核心服务 ──
    public NodeGraph Graph { get; }
    public NodeDiscovery Discovery { get; }
    public JsonGraphSerializer Serializer { get; }

    // ── UI 数据集合 ──
    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();

    /// <summary>按分类分组的可用节点描述（供节点面板显示）</summary>
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

    /// <summary>获取所有选中的节点（IsSelected == true）</summary>
    public List<NodeViewModel> GetSelectedNodes() => Nodes.Where(n => n.IsSelected).ToList();

    /// <summary>单选一个节点（清除其他选中）</summary>
    public void SelectNode(NodeViewModel node)
    {
        DeselectAll();
        node.IsSelected = true;
        SelectedNode = node;
    }

    /// <summary>切换节点选中状态（Ctrl+点击）</summary>
    public void ToggleNodeSelection(NodeViewModel node)
    {
        node.IsSelected = !node.IsSelected;
        if (node.IsSelected)
            SelectedNode = node;
        else if (SelectedNode == node)
            SelectedNode = GetSelectedNodes().LastOrDefault();
    }

    /// <summary>框选节点（替换当前选中）</summary>
    public void SelectNodesInRect(double x1, double y1, double x2, double y2)
    {
        DeselectAll();
        AddNodesInRectToSelection(x1, y1, x2, y2);
    }

    /// <summary>框选节点（追加到当前选中，用于 Ctrl+框选）</summary>
    public void AddNodesInRectToSelection(double x1, double y1, double x2, double y2)
    {
        var minX = Math.Min(x1, x2);
        var maxX = Math.Max(x1, x2);
        var minY = Math.Min(y1, y2);
        var maxY = Math.Max(y1, y2);
        const double nodeW = 180;
        const double nodeH = 80;

        NodeViewModel? last = null;
        foreach (var n in Nodes)
        {
            if (n.X + nodeW >= minX && n.X <= maxX && n.Y + nodeH >= minY && n.Y <= maxY)
            {
                n.IsSelected = true;
                last = n;
            }
        }

        if (last != null)
            SelectedNode = last;
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

    // ── 临时连线（拖拽创建连线时的预览） ──
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

    public void UpdatePendingPath()
    {
        OnPropertyChanged(nameof(PendingPathData));
    }

    // ── 命令 ──
    public ICommand CreateNodeCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand DeleteNodeCommand { get; }
    public ICommand DeleteConnectionCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ExecuteCommand { get; }
    public ICommand ClearCommand { get; }

    // ── 事件 ──
    /// <summary>请求保存文件对话框（View 层处理）</summary>
    public Func<string?>? RequestSavePath { get; set; }

    /// <summary>请求打开文件对话框（View 层处理）</summary>
    public Func<string?>? RequestOpenPath { get; set; }

    /// <summary>节点位置变化时触发（供小地图更新）</summary>
    public event Action? NodePositionsChanged;

    /// <summary>图导入完成后触发（供 View 刷新端口坐标）</summary>
    public event Action? GraphImported;

    private int _nodeCounter;

    public EditorViewModel()
    {
        Graph = new NodeGraph();
        Discovery = new NodeDiscovery();
        Serializer = new JsonGraphSerializer();

        // 扫描 Samples 程序集（包含自定义节点）
        Discovery.ScanAssembly(typeof(NodeEditor.Samples.MathNode).Assembly);
        RefreshAvailableNodes();

        // 注册图事件
        Graph.NodeAdded += OnNodeAdded;
        Graph.NodeRemoved += OnNodeRemoved;
        Graph.ConnectionAdded += OnConnectionAdded;
        Graph.ConnectionRemoved += OnConnectionRemoved;

        // 初始化命令
        CreateNodeCommand = new RelayCommand(param => CreateNode(param as string));
        DeleteSelectedCommand = new RelayCommand(() => DeleteSelectedNode());
        DeleteNodeCommand = new RelayCommand(param => DeleteNode(param as NodeViewModel));
        DeleteConnectionCommand = new RelayCommand(param => DeleteConnection(param as ConnectionViewModel));
        ExportCommand = new RelayCommand(() => Export());
        ImportCommand = new RelayCommand(() => Import());
        ExecuteCommand = new RelayCommand(() => ExecuteGraph());
        ClearCommand = new RelayCommand(() => ClearGraph());
    }

    /// <summary>刷新节点面板的分类列表</summary>
    private void RefreshAvailableNodes()
    {
        AvailableNodes.Clear();
        foreach (var (category, descriptors) in Discovery.GetByCategory())
        {
            AvailableNodes.Add(new NodeCategoryViewModel(category, descriptors));
        }
    }

    // ── 节点管理 ──

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
    }

    public void DeleteNode(NodeViewModel? nodeVm)
    {
        if (nodeVm == null) return;
        Graph.RemoveNode(nodeVm.Node);
    }

    public void DeleteSelectedNode()
    {
        // 删除所有选中的节点
        var selected = GetSelectedNodes();
        if (selected.Count == 0 && SelectedNode != null)
            selected.Add(SelectedNode);
        foreach (var nvm in selected)
            Graph.RemoveNode(nvm.Node);
    }

    private void OnNodePositionChanged()
    {
        NodePositionsChanged?.Invoke();
    }

    // ── 连线管理 ──

    /// <summary>尝试连接两个端口</summary>
    public bool TryConnect(PortViewModel source, PortViewModel target)
    {
        if (source.IsInput && target.IsOutput)
        {
            // 交换：确保 source 是输出，target 是输入
            (source, target) = (target, source);
        }

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
    }

    private void OnConnectionRemoved(NodeConnection conn)
    {
        var cvm = Connections.FirstOrDefault(c => c.Connection.Id == conn.Id);
        if (cvm != null)
        {
            cvm.Detach();
            Connections.Remove(cvm);

            // 刷新端口连接状态
            var sourceNodeVm = Nodes.FirstOrDefault(n => n.Id == conn.SourceNodeId);
            var targetNodeVm = Nodes.FirstOrDefault(n => n.Id == conn.TargetNodeId);
            sourceNodeVm?.GetPortViewModel(conn.SourcePortId)?.RefreshConnectionState();
            targetNodeVm?.GetPortViewModel(conn.TargetPortId)?.RefreshConnectionState();
        }
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

    public void CancelPendingConnection()
    {
        PendingSourcePort = null;
    }

    /// <summary>完成临时连线 — 尝试连接到目标端口</summary>
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

    public void Export()
    {
        var path = RequestSavePath?.Invoke();
        if (string.IsNullOrEmpty(path)) return;
        Serializer.SerializeToFile(Graph, path);
    }

    public void Import()
    {
        var path = RequestOpenPath?.Invoke();
        if (string.IsNullOrEmpty(path)) return;

        var newGraph = Serializer.DeserializeFromFile(path);

        // 清空当前图（事件仍订阅，OnNodeRemoved/OnConnectionRemoved 自动更新 UI 集合）
        ClearGraph();

        // 导入新图（ImportFrom 清除端口旧连接后重新创建，确保事件正常触发）
        Graph.ImportFrom(newGraph);
        GraphImported?.Invoke();
    }

    public void ExecuteGraph()
    {
        Graph.Execute();
    }

    public void ClearGraph()
    {
        Graph.Clear();
    }

    /// <summary>取消所有节点选中</summary>
    public void DeselectAll()
    {
        foreach (var n in Nodes)
            n.IsSelected = false;
        SelectedNode = null;
    }

    // ── 复制/粘贴 ──

    /// <summary>复制选中节点到剪贴板</summary>
    public void CopySelectedNodes()
    {
        var selected = GetSelectedNodes();
        if (selected.Count == 0) return;

        var nodeIds = new HashSet<string>(selected.Select(n => n.Id));
        var json = Serializer.SerializeNodes(Graph, nodeIds);
        Clipboard.SetText(json);
    }

    /// <summary>从剪贴板粘贴节点（偏移位置）</summary>
    public void PasteNodes(double offsetX, double offsetY)
    {
        var json = Clipboard.GetText();
        if (string.IsNullOrEmpty(json)) return;

        try
        {
            var pastedGraph = Serializer.DeserializeWithNewIds(json, offsetX, offsetY);
            DeselectAll();
            Graph.ImportFrom(pastedGraph);

            // 选中粘贴的节点
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
            /* 剪贴板内容无效 */
        }
    }
}

/// <summary>节点面板中的一个分类</summary>
public class NodeCategoryViewModel : ViewModelBase
{
    public string CategoryName { get; }
    public ObservableCollection<NodeDescriptor> Nodes { get; }

    public NodeCategoryViewModel(string categoryName, System.Collections.Generic.List<NodeDescriptor> nodes)
    {
        CategoryName = categoryName;
        Nodes = new ObservableCollection<NodeDescriptor>(nodes);
    }
}
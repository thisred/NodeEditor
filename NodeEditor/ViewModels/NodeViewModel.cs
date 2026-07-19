using System.Collections.ObjectModel;
using NodeEditor.Core.Models;

namespace NodeEditor.ViewModels;

/// <summary>
/// 节点视图模型 — 包装 NodeBase，提供 UI 绑定属性。
/// </summary>
public class NodeViewModel : ViewModelBase
{
    /// <summary>节点卡片渲染宽度（画布逻辑坐标），用于布局、框选与命中检测的统一基准。</summary>
    public const double ApproxWidth = 160;

    /// <summary>节点近似高度（画布逻辑坐标），用于框选相交判断与小地图缩略图。</summary>
    public const double ApproxHeight = 80;

    public NodeBase Node { get; }

    public string Id => Node.Id;
    public string DisplayName => Node.DisplayName;
    public string Category => Node.Category;
    public string Color => Node.Color;
    public string Description => Node.Description;

    /// <summary>节点在画布上的 X 坐标</summary>
    public double X
    {
        get => Node.X;
        set
        {
            if (Node.X == value) return;
            Node.X = value;
            OnPropertyChanged();
            OnPositionChanged();
        }
    }

    /// <summary>节点在画布上的 Y 坐标</summary>
    public double Y
    {
        get => Node.Y;
        set
        {
            if (Node.Y == value) return;
            Node.Y = value;
            OnPropertyChanged();
            OnPositionChanged();
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public ObservableCollection<PortViewModel> InputPorts { get; } = new();
    public ObservableCollection<PortViewModel> OutputPorts { get; } = new();
    public ObservableCollection<NodePropertyViewModel> Properties { get; } = new();

    /// <summary>节点位置变化时触发</summary>
    public event System.Action? PositionChanged;

    public NodeViewModel(NodeBase node)
    {
        Node = node;

        foreach (var port in node.InputPorts)
            InputPorts.Add(new PortViewModel(port));

        foreach (var port in node.OutputPorts)
            OutputPorts.Add(new PortViewModel(port));

        foreach (var prop in node.GetEditableProperties())
            Properties.Add(new NodePropertyViewModel(node, prop));
    }

    private void OnPositionChanged()
    {
        PositionChanged?.Invoke();
    }

    public PortViewModel? GetPortViewModel(string portId)
    {
        foreach (var p in InputPorts)
            if (p.PortId == portId) return p;
        foreach (var p in OutputPorts)
            if (p.PortId == portId) return p;
        return null;
    }

    public void RefreshPortStates()
    {
        foreach (var p in InputPorts) p.RefreshConnectionState();
        foreach (var p in OutputPorts) p.RefreshConnectionState();
    }
}

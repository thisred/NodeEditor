using System.Collections.ObjectModel;
using NodeEditor.Core.Models;

namespace NodeEditor.Wpf.ViewModels;

/// <summary>
/// 节点视图模型 — 包装 NodeBase，提供 UI 绑定属性。
/// 管理端口的 PortViewModel 列表，以及选中状态。
/// </summary>
public class NodeViewModel : ViewModelBase
{
    public NodeBase Node { get; }

    public string Id => Node.Id;
    public string DisplayName => Node.DisplayName;
    public string Category => Node.Category;
    public string Color => Node.Color;
    public string Description => Node.Description;

    /// <summary>节点在画布上的 X 坐标（双向同步到 NodeBase）</summary>
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

    /// <summary>节点在画布上的 Y 坐标（双向同步到 NodeBase）</summary>
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

    /// <summary>输入端口列表</summary>
    public ObservableCollection<PortViewModel> InputPorts { get; } = new();

    /// <summary>输出端口列表</summary>
    public ObservableCollection<PortViewModel> OutputPorts { get; } = new();

    /// <summary>可编辑属性列表</summary>
    public ObservableCollection<NodePropertyViewModel> Properties { get; } = new();

    /// <summary>节点位置变化时触发（通知连线更新）</summary>
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

    /// <summary>根据端口 ID 查找 PortViewModel</summary>
    public PortViewModel? GetPortViewModel(string portId)
    {
        foreach (var p in InputPorts)
            if (p.PortId == portId)
                return p;
        foreach (var p in OutputPorts)
            if (p.PortId == portId)
                return p;
        return null;
    }

    /// <summary>刷新所有端口的连接状态</summary>
    public void RefreshPortStates()
    {
        foreach (var p in InputPorts) p.RefreshConnectionState();
        foreach (var p in OutputPorts) p.RefreshConnectionState();
    }
}
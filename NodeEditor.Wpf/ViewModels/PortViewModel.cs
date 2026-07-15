using NodeEditor.Core.Models;

namespace NodeEditor.Wpf.ViewModels;

/// <summary>
/// 端口视图模型 — 包装 NodePort，并维护端口在画布上的坐标位置。
/// 坐标由 View 层（端口控件 LayoutUpdated）回写，供 ConnectionViewModel 计算连线路径。
/// </summary>
public class PortViewModel : ViewModelBase
{
    public NodePort Port { get; }

    public string DisplayName => Port.DisplayName;
    public string PortTypeName => Port.PortType.Name;
    public bool IsInput => Port.Direction == PortDirection.Input;
    public bool IsOutput => Port.Direction == PortDirection.Output;
    public bool IsExec => Port.Kind == PortKind.Exec;
    public string NodeId => Port.NodeId;
    public string PortId => Port.Id;
    public bool IsConnected => Port.IsConnected;

    /// <summary>端口在画布坐标系中的中心 X（View 层回写）</summary>
    public double CenterX
    {
        get => _centerX;
        set => Set(ref _centerX, value);
    }

    private double _centerX;

    /// <summary>端口在画布坐标系中的中心 Y（View 层回写）</summary>
    public double CenterY
    {
        get => _centerY;
        set => Set(ref _centerY, value);
    }

    private double _centerY;

    /// <summary>端口是否已被定位（View 层首次渲染后设为 true）</summary>
    public bool IsPositionValid
    {
        get => _isPositionValid;
        set => Set(ref _isPositionValid, value);
    }

    private bool _isPositionValid;

    public PortViewModel(NodePort port)
    {
        Port = port;
    }

    public void RefreshConnectionState()
    {
        OnPropertyChanged(nameof(IsConnected));
    }
}
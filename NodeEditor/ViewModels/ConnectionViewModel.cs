using NodeEditor.Core.Models;

namespace NodeEditor.ViewModels;

/// <summary>
/// 连线视图模型 — 包装 NodeConnection，计算贝塞尔曲线路径。
/// </summary>
public class ConnectionViewModel : ViewModelBase
{
    public NodeConnection Connection { get; }
    public PortViewModel SourcePort { get; }
    public PortViewModel TargetPort { get; }

    public double X1 => SourcePort.CenterX;
    public double Y1 => SourcePort.CenterY;
    public double X2 => TargetPort.CenterX;
    public double Y2 => TargetPort.CenterY;

    /// <summary>贝塞尔曲线 Path Data（SVG 格式）</summary>
    public string PathData
    {
        get
        {
            var midX = (X1 + X2) / 2.0;
            return $"M {X1},{Y1} C {midX},{Y1} {midX},{Y2} {X2},{Y2}";
        }
    }

    public ConnectionViewModel(NodeConnection connection, PortViewModel sourcePort, PortViewModel targetPort)
    {
        Connection = connection;
        SourcePort = sourcePort;
        TargetPort = targetPort;

        SourcePort.PropertyChanged += OnPortPositionChanged;
        TargetPort.PropertyChanged += OnPortPositionChanged;
    }

    private void OnPortPositionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(PortViewModel.CenterX) or nameof(PortViewModel.CenterY)))
            return;
        OnPropertyChanged(nameof(X1));
        OnPropertyChanged(nameof(Y1));
        OnPropertyChanged(nameof(X2));
        OnPropertyChanged(nameof(Y2));
        OnPropertyChanged(nameof(PathData));
    }

    public void Detach()
    {
        SourcePort.PropertyChanged -= OnPortPositionChanged;
        TargetPort.PropertyChanged -= OnPortPositionChanged;
    }
}

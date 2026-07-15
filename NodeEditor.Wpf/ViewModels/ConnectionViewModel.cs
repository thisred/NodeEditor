using System;
using NodeEditor.Core.Models;

namespace NodeEditor.Wpf.ViewModels;

/// <summary>
/// 连线视图模型 — 包装 NodeConnection，计算贝塞尔曲线路径。
/// 监听源/目标端口的位置变化，自动更新连线路径。
/// </summary>
public class ConnectionViewModel : ViewModelBase
{
    public NodeConnection Connection { get; }
    public PortViewModel SourcePort { get; }
    public PortViewModel TargetPort { get; }

    /// <summary>起点 X（源端口位置）</summary>
    public double X1 => SourcePort.CenterX;

    /// <summary>起点 Y</summary>
    public double Y1 => SourcePort.CenterY;

    /// <summary>终点 X（目标端口位置）</summary>
    public double X2 => TargetPort.CenterX;

    /// <summary>终点 Y</summary>
    public double Y2 => TargetPort.CenterY;

    /// <summary>贝塞尔曲线 Path Data</summary>
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

        // 通知所有坐标和路径属性更新
        OnPropertyChanged(nameof(X1));
        OnPropertyChanged(nameof(Y1));
        OnPropertyChanged(nameof(X2));
        OnPropertyChanged(nameof(Y2));
        OnPropertyChanged(nameof(PathData));
    }

    /// <summary>断开事件监听（删除连线时调用）</summary>
    public void Detach()
    {
        SourcePort.PropertyChanged -= OnPortPositionChanged;
        TargetPort.PropertyChanged -= OnPortPositionChanged;
    }
}
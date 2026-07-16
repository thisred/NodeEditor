using System.Collections.Generic;
using System.Windows.Media;
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

    /// <summary>端口类型对应的颜色画刷（按数据类型着色）</summary>
    public SolidColorBrush TypeColor => GetTypeBrush(Port.PortType, IsExec);

    /// <summary>类型显示名（小写友好名，Exec 端口返回空字符串）</summary>
    public string TypeDisplayName => IsExec ? "" : GetFriendlyTypeName(Port.PortType.Name);

    // ── 类型颜色映射 ──

    private static readonly SolidColorBrush ExecBrush = Frozen(0xF0, 0xF0, 0xF0);
    private static readonly SolidColorBrush FallbackBrush = Frozen(0x88, 0x88, 0x88);
    private static readonly Dictionary<string, SolidColorBrush> TypeBrushes = new()
    {
        ["Double"]  = Frozen(0x6D, 0xBE, 0x45),   // 绿 — 浮点
        ["Single"]  = Frozen(0x6D, 0xBE, 0x45),
        ["Int32"]   = Frozen(0x4E, 0xC9, 0xB0),    // 青 — 整数
        ["Int64"]   = Frozen(0x4E, 0xC9, 0xB0),
        ["Int16"]   = Frozen(0x4E, 0xC9, 0xB0),
        ["Byte"]    = Frozen(0x4E, 0xC9, 0xB0),
        ["String"]  = Frozen(0xCE, 0x91, 0x78),    // 橙红 — 字符串
        ["Char"]    = Frozen(0xCE, 0x91, 0x78),
        ["Boolean"] = Frozen(0xC5, 0x86, 0xC0),    // 紫 — 布尔
    };

    private static SolidColorBrush GetTypeBrush(Type portType, bool isExec)
        => isExec ? ExecBrush : (TypeBrushes.TryGetValue(portType.Name, out var b) ? b : FallbackBrush);

    private static string GetFriendlyTypeName(string typeName) => typeName switch
    {
        "Double"  => "double",
        "Single"  => "float",
        "Int32"   => "int",
        "Int64"   => "long",
        "Int16"   => "short",
        "Byte"    => "byte",
        "String"  => "string",
        "Char"    => "char",
        "Boolean" => "bool",
        "Object"  => "object",
        _         => typeName.ToLowerInvariant(),
    };

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

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
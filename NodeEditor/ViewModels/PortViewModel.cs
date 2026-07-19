using System.Collections.Generic;
using NodeEditor.Core.Models;

namespace NodeEditor.ViewModels;

/// <summary>
/// 端口视图模型 — 包装 NodePort，并维护端口在画布上的坐标位置。
/// 坐标由 View 层回写，供 ConnectionViewModel 计算连线路径。
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

    /// <summary>端口类型对应的颜色（按数据类型着色）</summary>
    public Color TypeColor => GetTypeColor(Port.PortType, IsExec);

    /// <summary>类型显示名（小写友好名，Exec 端口返回空字符串）</summary>
    public string TypeDisplayName => IsExec ? "" : GetFriendlyTypeName(Port.PortType.Name);

    // ── 类型颜色映射 ──

    private static readonly Color ExecColor = Color.FromRgb(0xF0, 0xF0, 0xF0);
    private static readonly Color FallbackColor = Color.FromRgb(0x88, 0x88, 0x88);
    private static readonly Dictionary<string, Color> TypeColors = new()
    {
        ["Double"]  = Color.FromRgb(0x6D, 0xBE, 0x45),
        ["Single"]  = Color.FromRgb(0x6D, 0xBE, 0x45),
        ["Int32"]   = Color.FromRgb(0x4E, 0xC9, 0xB0),
        ["Int64"]   = Color.FromRgb(0x4E, 0xC9, 0xB0),
        ["Int16"]   = Color.FromRgb(0x4E, 0xC9, 0xB0),
        ["Byte"]    = Color.FromRgb(0x4E, 0xC9, 0xB0),
        ["String"]  = Color.FromRgb(0xCE, 0x91, 0x78),
        ["Char"]    = Color.FromRgb(0xCE, 0x91, 0x78),
        ["Boolean"] = Color.FromRgb(0xC5, 0x86, 0xC0),
    };

    private static Color GetTypeColor(Type portType, bool isExec)
        => isExec ? ExecColor : (TypeColors.TryGetValue(portType.Name, out var c) ? c : FallbackColor);

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

    /// <summary>端口是否已被定位</summary>
    public bool IsPositionValid
    {
        get => _isPositionValid;
        set => Set(ref _isPositionValid, value);
    }
    private bool _isPositionValid;

    /// <summary>
    /// 静默设置端口位置（不触发 PropertyChanged，用于批量更新时避免事件风暴）。
    /// 调用方负责在批量更新后统一刷新连线。
    /// </summary>
    public void SetPositionSilent(double x, double y)
    {
        _centerX = x;
        _centerY = y;
        _isPositionValid = true;
    }

    public PortViewModel(NodePort port)
    {
        Port = port;
    }

    public void RefreshConnectionState()
    {
        OnPropertyChanged(nameof(IsConnected));
    }
}

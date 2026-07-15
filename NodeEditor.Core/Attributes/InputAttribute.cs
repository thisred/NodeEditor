using System;

namespace NodeEditor.Core.Attributes;

/// <summary>
/// 标记一个 NodePort 属性为输入端口。
/// 基类构造时会自动读取此特性并初始化端口方向和类型。
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class InputAttribute : Attribute
{
    /// <summary>端口显示名称</summary>
    public string DisplayName { get; }

    /// <summary>端口接受的数据类型</summary>
    public Type PortType { get; }

    /// <summary>是否允许多个连接接入此端口（默认 false，只允许单输入）</summary>
    public bool AllowMultiple { get; set; } = false;

    public InputAttribute(string displayName, Type portType)
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        PortType = portType ?? throw new ArgumentNullException(nameof(portType));
    }
}
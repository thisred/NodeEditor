using System;

namespace NodeEditor.Core.Attributes;

/// <summary>
/// 标记一个 NodePort 属性为输出端口。
/// 输出端口默认允许多个连接输出（一个输出可连到多个输入）。
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class OutputAttribute : Attribute
{
    /// <summary>端口显示名称</summary>
    public string DisplayName { get; }

    /// <summary>端口输出的数据类型</summary>
    public Type PortType { get; }

    public OutputAttribute(string displayName, Type portType)
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        PortType = portType ?? throw new ArgumentNullException(nameof(portType));
    }
}
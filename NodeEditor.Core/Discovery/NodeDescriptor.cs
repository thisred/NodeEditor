using System;
using System.Collections.Generic;
using NodeEditor.Core.Models;

namespace NodeEditor.Core.Discovery;

/// <summary>
/// 节点类型描述 — 不实例化节点即可获取的元数据信息。
/// 用于编辑器节点面板展示和节点创建。
/// </summary>
public class NodeDescriptor
{
    /// <summary>节点类型全名（唯一标识，用于创建实例）</summary>
    public string TypeId { get; set; } = string.Empty;

    /// <summary>显示名称</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>分类路径</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>颜色</summary>
    public string Color { get; set; } = "#5A5A5A";

    /// <summary>描述</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>节点种类</summary>
    public NodeKind Kind { get; set; } = NodeKind.Get;

    /// <summary>输入端口描述</summary>
    public List<PortDescriptor> InputPorts { get; set; } = new();

    /// <summary>输出端口描述</summary>
    public List<PortDescriptor> OutputPorts { get; set; } = new();

    /// <summary>可编辑属性描述</summary>
    public List<PropertyDescriptor> Properties { get; set; } = new();
}

/// <summary>端口描述</summary>
public class PortDescriptor
{
    public string PropertyName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PortTypeName { get; set; } = string.Empty;
    public bool AllowMultiple { get; set; }
    public PortKind Kind { get; set; } = PortKind.Data;
}

/// <summary>可编辑属性描述</summary>
public class PropertyDescriptor
{
    public string PropertyName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PropertyTypeName { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public int Order { get; set; }
}
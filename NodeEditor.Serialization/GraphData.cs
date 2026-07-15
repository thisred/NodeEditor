using System.Collections.Generic;

namespace NodeEditor.Serialization;

/// <summary>
/// 序列化后的图数据（DTO），与 NodeGraph 模型解耦。
/// 端口连接通过"端口属性名"而非随机端口 ID 引用，保证跨会话稳定。
/// </summary>
public class SerializedGraph
{
    public string Version { get; set; } = "1.0";
    public List<SerializedNode> Nodes { get; set; } = new();
    public List<SerializedConnection> Connections { get; set; } = new();
}

/// <summary>序列化后的节点数据</summary>
public class SerializedNode
{
    /// <summary>节点 ID（恢复时保持一致）</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>节点类型全名（AssemblyQualifiedName）</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>X 坐标</summary>
    public double X { get; set; }

    /// <summary>Y 坐标</summary>
    public double Y { get; set; }

    /// <summary>可编辑属性名 → 值（枚举存为字符串）</summary>
    public Dictionary<string, object?> Properties { get; set; } = new();
}

/// <summary>序列化后的连线数据</summary>
public class SerializedConnection
{
    /// <summary>源节点 ID</summary>
    public string SourceNodeId { get; set; } = string.Empty;

    /// <summary>源端口的属性名（如 "Result"）</summary>
    public string SourcePortName { get; set; } = string.Empty;

    /// <summary>目标节点 ID</summary>
    public string TargetNodeId { get; set; } = string.Empty;

    /// <summary>目标端口的属性名（如 "InputA"）</summary>
    public string TargetPortName { get; set; } = string.Empty;
}
using System;

namespace NodeEditor.Core.Models;

/// <summary>
/// 节点之间的连线 — 从一个输出端口连接到一个输入端口。
/// 连线是单向的：Source（输出端口） → Target（输入端口）。
/// </summary>
public class NodeConnection
{
    /// <summary>连线唯一 ID</summary>
    public string Id { get; }

    /// <summary>源节点 ID（输出端）</summary>
    public string SourceNodeId { get; }

    /// <summary>源端口 ID（输出端口）</summary>
    public string SourcePortId { get; }

    /// <summary>目标节点 ID（输入端）</summary>
    public string TargetNodeId { get; }

    /// <summary>目标端口 ID（输入端口）</summary>
    public string TargetPortId { get; }

    /// <summary>源节点引用（运行时便捷访问）</summary>
    public NodeBase? SourceNode { get; internal set; }

    /// <summary>目标节点引用（运行时便捷访问）</summary>
    public NodeBase? TargetNode { get; internal set; }

    public NodeConnection(string sourceNodeId, string sourcePortId, string targetNodeId, string targetPortId)
    {
        Id = Guid.NewGuid().ToString("N");
        SourceNodeId = sourceNodeId;
        SourcePortId = sourcePortId;
        TargetNodeId = targetNodeId;
        TargetPortId = targetPortId;
    }

    /// <summary>获取源端口对象</summary>
    public NodePort? GetSourcePort() => SourceNode?.GetPort(SourcePortId);

    /// <summary>获取目标端口对象</summary>
    public NodePort? GetTargetPort() => TargetNode?.GetPort(TargetPortId);
}
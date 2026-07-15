using System;
using System.Collections.Generic;

namespace NodeEditor.Core.Models;

/// <summary>
/// 节点端口 — 数据流入或流出的连接点。
/// 每个端口有明确的数据类型，只有类型兼容的端口才能互相连接。
/// </summary>
public class NodePort
{
    /// <summary>端口唯一 ID</summary>
    public string Id { get; }

    /// <summary>端口显示名称</summary>
    public string DisplayName { get; }

    /// <summary>端口接受/输出的数据类型</summary>
    public Type PortType { get; }

    /// <summary>端口方向</summary>
    public PortDirection Direction { get; }

    /// <summary>端口种类（数据 / 执行）</summary>
    public PortKind Kind { get; }

    /// <summary>所属节点 ID</summary>
    public string NodeId { get; internal set; } = string.Empty;

    /// <summary>是否允许多个连接</summary>
    public bool AllowMultiple { get; internal set; }

    /// <summary>连接到该端口的所有连线</summary>
    public List<NodeConnection> Connections { get; } = new();

    /// <summary>
    /// 端口当前值（未连接时的本地值或执行后产生的值）。
    /// 输入端口未连接时可直接设置此值；输出端口在节点执行后设置。
    /// </summary>
    public object? Value { get; set; }

    /// <summary>该端口是否已有连接</summary>
    public bool IsConnected => Connections.Count > 0;

    public NodePort(string displayName, Type portType, PortDirection direction, bool allowMultiple = false, PortKind kind = PortKind.Data)
    {
        Id = Guid.NewGuid().ToString("N");
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        PortType = portType ?? throw new ArgumentNullException(nameof(portType));
        Direction = direction;
        AllowMultiple = direction == PortDirection.Output || allowMultiple;
        Kind = kind;
    }

    /// <summary>尝试获取连接的对端端口</summary>
    public NodePort? GetConnectedPort()
    {
        if (Connections.Count == 0) return null;
        var conn = Connections[0];
        return conn.SourcePortId == Id ? conn.TargetNode?.GetPort(conn.TargetPortId) : conn.SourceNode?.GetPort(conn.SourcePortId);
    }

    internal void AddConnection(NodeConnection connection)
    {
        Connections.Add(connection);
    }

    internal void RemoveConnection(NodeConnection connection)
    {
        Connections.Remove(connection);
    }

    internal void ClearConnections()
    {
        Connections.Clear();
    }
}
using System;
using System.Collections.Generic;
using System.Linq;

namespace NodeEditor.Core.Models;

/// <summary>
/// 节点图 — 包含所有节点和连线的顶层容器。
/// 负责节点的增删、连线的建立/断开（含类型验证）、以及图的拓扑遍历。
/// </summary>
public class NodeGraph
{
    /// <summary>图中所有节点</summary>
    public List<NodeBase> Nodes { get; } = new();

    /// <summary>图中所有连线</summary>
    public List<NodeConnection> Connections { get; } = new();

    /// <summary>节点添加后触发</summary>
    public event Action<NodeBase>? NodeAdded;

    /// <summary>节点移除后触发</summary>
    public event Action<NodeBase>? NodeRemoved;

    /// <summary>连线添加后触发</summary>
    public event Action<NodeConnection>? ConnectionAdded;

    /// <summary>连线移除后触发</summary>
    public event Action<NodeConnection>? ConnectionRemoved;

    /// <summary>添加节点到图中</summary>
    public void AddNode(NodeBase node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        Nodes.Add(node);
        NodeAdded?.Invoke(node);
    }

    /// <summary>从图中移除节点（同时移除其所有连线）</summary>
    public void RemoveNode(NodeBase node)
    {
        if (node == null || !Nodes.Contains(node)) return;

        // 移除所有关联的连线
        var relatedConnections = Connections
            .Where(c => c.SourceNodeId == node.Id || c.TargetNodeId == node.Id)
            .ToList();
        foreach (var conn in relatedConnections)
        {
            RemoveConnection(conn);
        }

        Nodes.Remove(node);
        NodeRemoved?.Invoke(node);
    }

    /// <summary>根据 ID 查找节点</summary>
    public NodeBase? GetNode(string nodeId) => Nodes.FirstOrDefault(n => n.Id == nodeId);

    /// <summary>
    /// 尝试创建连线，自动进行类型兼容性检查和方向验证。
    /// 返回 null 表示连接失败（可通过 validateOnly 预检查）。
    /// </summary>
    public NodeConnection? TryConnect(string sourceNodeId, string sourcePortId, string targetNodeId, string targetPortId)
    {
        if (!CanConnect(sourceNodeId, sourcePortId, targetNodeId, targetPortId, out var reason))
            return null;

        return Connect(sourceNodeId, sourcePortId, targetNodeId, targetPortId);
    }

    /// <summary>
    /// 检查两个端口是否可以连接。
    /// 规则：源必须为输出端口，目标必须为输入端口；类型必须兼容；不能自环；目标端口如果不允许多连接则不能已有连接。
    /// </summary>
    public bool CanConnect(string sourceNodeId, string sourcePortId, string targetNodeId, string targetPortId, out string reason)
    {
        reason = string.Empty;

        var sourceNode = GetNode(sourceNodeId);
        var targetNode = GetNode(targetNodeId);
        if (sourceNode == null || targetNode == null)
        {
            reason = "节点不存在";
            return false;
        }

        var sourcePort = sourceNode.GetPort(sourcePortId);
        var targetPort = targetNode.GetPort(targetPortId);
        if (sourcePort == null || targetPort == null)
        {
            reason = "端口不存在";
            return false;
        }

        // 方向检查
        if (sourcePort.Direction != PortDirection.Output || targetPort.Direction != PortDirection.Input)
        {
            reason = "连线必须从输出端口连接到输入端口";
            return false;
        }

        // 自环检查
        if (sourceNodeId == targetNodeId)
        {
            reason = "不能连接到自身";
            return false;
        }

        // 端口种类检查：执行端口只能连执行端口，数据端口只能连数据端口
        if (sourcePort.Kind != targetPort.Kind)
        {
            reason = "执行端口和数据端口不能互相连接";
            return false;
        }

        // 类型兼容性检查（仅对数据端口）
        if (sourcePort.Kind == PortKind.Data && !IsTypeCompatible(sourcePort.PortType, targetPort.PortType))
        {
            reason = $"类型不兼容：{sourcePort.PortType.Name} → {targetPort.PortType.Name}";
            return false;
        }

        // 目标端口多连接检查
        if (!targetPort.AllowMultiple && targetPort.IsConnected)
        {
            reason = "目标端口已存在连接且不允许多连接";
            return false;
        }

        // 重复连接检查
        var exists = Connections.Any(c =>
            c.SourcePortId == sourcePortId && c.TargetPortId == targetPortId);
        if (exists)
        {
            reason = "连线已存在";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 直接创建连线（不做验证，内部使用）。外部调用应使用 TryConnect。
    /// </summary>
    private NodeConnection Connect(string sourceNodeId, string sourcePortId, string targetNodeId, string targetPortId)
    {
        var sourceNode = GetNode(sourceNodeId)!;
        var targetNode = GetNode(targetNodeId)!;
        var sourcePort = sourceNode.GetPort(sourcePortId)!;
        var targetPort = targetNode.GetPort(targetPortId)!;

        // 如果目标端口不允许多连接且已有连接，先移除旧连接
        if (!targetPort.AllowMultiple && targetPort.IsConnected)
        {
            var oldConn = Connections.First(c => c.TargetPortId == targetPortId);
            RemoveConnection(oldConn);
        }

        var connection = new NodeConnection(sourceNodeId, sourcePortId, targetNodeId, targetPortId)
        {
            SourceNode = sourceNode,
            TargetNode = targetNode
        };

        Connections.Add(connection);
        sourcePort.AddConnection(connection);
        targetPort.AddConnection(connection);
        ConnectionAdded?.Invoke(connection);
        return connection;
    }

    /// <summary>移除连线</summary>
    public void RemoveConnection(NodeConnection connection)
    {
        if (connection == null || !Connections.Contains(connection)) return;

        var sourcePort = connection.GetSourcePort();
        var targetPort = connection.GetTargetPort();
        sourcePort?.RemoveConnection(connection);
        targetPort?.RemoveConnection(connection);

        Connections.Remove(connection);
        ConnectionRemoved?.Invoke(connection);
    }

    /// <summary>根据 ID 移除连线</summary>
    public void RemoveConnection(string connectionId)
    {
        var conn = Connections.FirstOrDefault(c => c.Id == connectionId);
        if (conn != null) RemoveConnection(conn);
    }

    /// <summary>
    /// 获取节点的所有上游节点（通过输入端口连接的节点）
    /// </summary>
    public List<NodeBase> GetUpstreamNodes(NodeBase node)
    {
        var upstream = new List<NodeBase>();
        foreach (var port in node.InputPorts)
        {
            foreach (var conn in port.Connections)
            {
                if (conn.SourceNode != null && !upstream.Contains(conn.SourceNode))
                    upstream.Add(conn.SourceNode);
            }
        }

        return upstream;
    }

    /// <summary>
    /// 获取节点的所有下游节点（通过输出端口连接的节点）
    /// </summary>
    public List<NodeBase> GetDownstreamNodes(NodeBase node)
    {
        var downstream = new List<NodeBase>();
        foreach (var port in node.OutputPorts)
        {
            foreach (var conn in port.Connections)
            {
                if (conn.TargetNode != null && !downstream.Contains(conn.TargetNode))
                    downstream.Add(conn.TargetNode);
            }
        }

        return downstream;
    }

    /// <summary>
    /// 拓扑排序 — 返回执行顺序（从无输入依赖的节点开始）。
    /// 如果存在环，环中的节点不会出现在结果中。
    /// </summary>
    public List<NodeBase> TopologicalSort()
    {
        var inDegree = Nodes.ToDictionary(n => n.Id, _ => 0);
        foreach (var conn in Connections)
        {
            if (inDegree.ContainsKey(conn.TargetNodeId))
                inDegree[conn.TargetNodeId]++;
        }

        var queue = new Queue<NodeBase>();
        foreach (var node in Nodes)
        {
            if (inDegree[node.Id] == 0)
                queue.Enqueue(node);
        }

        var result = new List<NodeBase>();
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            result.Add(node);

            foreach (var downstream in GetDownstreamNodes(node))
            {
                inDegree[downstream.Id]--;
                if (inDegree[downstream.Id] == 0)
                    queue.Enqueue(downstream);
            }
        }

        return result;
    }

    /// <summary>
    /// 执行节点图。
    /// 如果图中存在事件节点（Event），则按控制流模型执行：
    ///   1. 先按拓扑顺序执行所有数据节点（Get）
    ///   2. 再从每个事件节点出发，沿执行端口链依次执行
    /// 如果没有事件节点，则回退到纯拓扑排序执行（向后兼容）。
    /// </summary>
    public void Execute()
    {
        var hasEvents = Nodes.Any(n => n.Kind == NodeKind.Event);

        if (!hasEvents)
        {
            // 向后兼容：纯数据流模式
            foreach (var node in TopologicalSort())
            {
                node.Execute();
            }

            return;
        }

        // ── 控制流 + 数据流模式 ──

        // 1. 执行所有数据节点（Get），按拓扑顺序
        var execChainNodeIds = new HashSet<string>(
            Nodes.Where(n => n.Kind is NodeKind.Event or NodeKind.Action or NodeKind.Task)
                .Select(n => n.Id)
        );

        foreach (var node in TopologicalSort().Where(n => !execChainNodeIds.Contains(n.Id)))
        {
            node.Execute();
        }

        // 2. 从事件节点出发，沿执行端口链执行
        //    先执行 OnStart 链，再执行 OnEnd 链
        var visited = new HashSet<string>();
        var eventNodes = Nodes.Where(n => n.Kind == NodeKind.Event).ToList();

        // Start 事件优先（OnStart、Begin 等）
        foreach (var eventNode in eventNodes.Where(n => !IsEndEvent(n)))
            ExecuteFromNode(eventNode, visited);

        // End 事件最后（OnEnd、End 等）
        foreach (var eventNode in eventNodes.Where(IsEndEvent))
            ExecuteFromNode(eventNode, visited);
    }

    /// <summary>
    /// 从指定节点开始，沿执行输出端口递归执行。
    /// 已执行的节点不会重复执行（防止环）。
    /// </summary>
    private void ExecuteFromNode(NodeBase node, HashSet<string> visited)
    {
        if (visited.Contains(node.Id)) return;
        visited.Add(node.Id);

        node.Execute();

        // 沿执行输出端口继续
        foreach (var port in node.OutputPorts.Where(p => p.Kind == PortKind.Exec))
        {
            foreach (var conn in port.Connections)
            {
                if (conn.TargetNode != null)
                    ExecuteFromNode(conn.TargetNode, visited);
            }
        }
    }

    /// <summary>
    /// 判断是否为“结束”事件（DisplayName 包含 End）。
    /// </summary>
    private static bool IsEndEvent(NodeBase node) =>
        node.DisplayName.Contains("End", StringComparison.OrdinalIgnoreCase);

    /// <summary>清空图中所有节点和连线</summary>
    public void Clear()
    {
        foreach (var conn in Connections.ToList())
            RemoveConnection(conn);
        foreach (var node in Nodes.ToList())
            RemoveNode(node);
    }

    /// <summary>
    /// 从另一个图导入所有节点和连线。
    /// 先清除源图端口上的连接引用（避免 CanConnect 校验失败），
    /// 再通过 AddNode + TryConnect 重新创建，确保事件正常触发。
    /// </summary>
    public void ImportFrom(NodeGraph source)
    {
        // 清除源图端口上的已有连接引用
        foreach (var node in source.Nodes)
        foreach (var port in node.Ports)
            port.ClearConnections();

        // 添加节点（触发 NodeAdded）
        foreach (var node in source.Nodes)
            AddNode(node);

        // 重新创建连线（触发 ConnectionAdded）
        foreach (var conn in source.Connections)
            TryConnect(conn.SourceNodeId, conn.SourcePortId, conn.TargetNodeId, conn.TargetPortId);
    }

    /// <summary>
    /// 类型兼容性检查：目标类型可以是源类型的基类/接口，或完全相同。
    /// 支持 object 类型作为通配（任何类型都可连接）。
    /// </summary>
    public static bool IsTypeCompatible(Type sourceType, Type targetType)
    {
        if (targetType == typeof(object) || sourceType == typeof(object))
            return true;
        return targetType.IsAssignableFrom(sourceType);
    }
}
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using NodeEditor.Core.Models;

namespace NodeEditor.Serialization;

/// <summary>
/// 图的 JSON 序列化器 — 负责将 NodeGraph 导出为 JSON / 从 JSON 导入。
/// 使用端口属性名（而非随机 ID）标识连线，保证序列化数据的稳定性。
/// </summary>
public class JsonGraphSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// 将节点图序列化为 JSON 字符串
    /// </summary>
    public string Serialize(NodeGraph graph)
    {
        var data = ToSerializedGraph(graph);
        return JsonSerializer.Serialize(data, JsonOptions);
    }

    /// <summary>
    /// 将节点图序列化并写入文件
    /// </summary>
    public void SerializeToFile(NodeGraph graph, string filePath)
    {
        var json = Serialize(graph);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// 从 JSON 字符串反序列化为节点图
    /// </summary>
    public NodeGraph Deserialize(string json)
    {
        var data = JsonSerializer.Deserialize<SerializedGraph>(json, JsonOptions);
        if (data == null) throw new InvalidOperationException("反序列化失败：数据为空");
        return FromSerializedGraph(data);
    }

    /// <summary>
    /// 序列化图中指定节点子集（用于复制）
    /// 只包含指定节点之间的连线，不含外部连线。
    /// </summary>
    public string SerializeNodes(NodeGraph graph, HashSet<string> nodeIds)
    {
        var data = new SerializedGraph();

        foreach (var node in graph.Nodes.Where(n => nodeIds.Contains(n.Id)))
        {
            var serializedNode = new SerializedNode
            {
                Id = node.Id,
                TypeName = node.TypeName,
                X = node.X,
                Y = node.Y
            };
            foreach (var (name, value) in node.GetPropertyValues())
                serializedNode.Properties[name] = SerializeValue(value);
            data.Nodes.Add(serializedNode);
        }

        foreach (var conn in graph.Connections)
        {
            if (!nodeIds.Contains(conn.SourceNodeId) || !nodeIds.Contains(conn.TargetNodeId))
                continue;

            var sourceNode = graph.GetNode(conn.SourceNodeId);
            var targetNode = graph.GetNode(conn.TargetNodeId);
            if (sourceNode == null || targetNode == null) continue;

            var sourcePort = sourceNode.GetPort(conn.SourcePortId);
            var targetPort = targetNode.GetPort(conn.TargetPortId);
            if (sourcePort == null || targetPort == null) continue;

            data.Connections.Add(new SerializedConnection
            {
                SourceNodeId = conn.SourceNodeId,
                SourcePortName = sourceNode.GetPortName(sourcePort)!,
                TargetNodeId = conn.TargetNodeId,
                TargetPortName = targetNode.GetPortName(targetPort)!
            });
        }

        return JsonSerializer.Serialize(data, JsonOptions);
    }

    /// <summary>
    /// 反序列化并生成新 ID（用于粘贴），同时偏移位置
    /// </summary>
    public NodeGraph DeserializeWithNewIds(string json, double offsetX, double offsetY)
    {
        var data = JsonSerializer.Deserialize<SerializedGraph>(json, JsonOptions);
        if (data == null) throw new InvalidOperationException("反序列化失败：数据为空");

        var graph = new NodeGraph();
        var idMap = new Dictionary<string, string>();

        foreach (var sn in data.Nodes)
        {
            var type = Type.GetType(sn.TypeName);
            if (type == null) continue;

            var node = Activator.CreateInstance(type) as NodeBase;
            if (node == null) continue;

            var newId = Guid.NewGuid().ToString();
            idMap[sn.Id] = newId;
            node.Id = newId;
            node.X = sn.X + offsetX;
            node.Y = sn.Y + offsetY;

            foreach (var (name, value) in sn.Properties)
                node.SetPropertyValue(name, DeserializeValue(value));

            graph.AddNode(node);
        }

        foreach (var sc in data.Connections)
        {
            if (!idMap.TryGetValue(sc.SourceNodeId, out var newSourceId) ||
                !idMap.TryGetValue(sc.TargetNodeId, out var newTargetId))
                continue;

            var sourceNode = graph.GetNode(newSourceId);
            var targetNode = graph.GetNode(newTargetId);
            if (sourceNode == null || targetNode == null) continue;

            var sourcePort = sourceNode.GetPortByName(sc.SourcePortName);
            var targetPort = targetNode.GetPortByName(sc.TargetPortName);
            if (sourcePort == null || targetPort == null) continue;

            graph.TryConnect(sourceNode.Id, sourcePort.Id, targetNode.Id, targetPort.Id);
        }

        return graph;
    }

    /// <summary>
    /// 从文件反序列化为节点图
    /// </summary>
    public NodeGraph DeserializeFromFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return Deserialize(json);
    }

    /// <summary>NodeGraph → SerializedGraph DTO</summary>
    private static SerializedGraph ToSerializedGraph(NodeGraph graph)
    {
        var result = new SerializedGraph();

        foreach (var node in graph.Nodes)
        {
            var serializedNode = new SerializedNode
            {
                Id = node.Id,
                TypeName = node.TypeName,
                X = node.X,
                Y = node.Y
            };

            // 序列化可编辑属性值（枚举转字符串）
            foreach (var (name, value) in node.GetPropertyValues())
            {
                serializedNode.Properties[name] = SerializeValue(value);
            }

            result.Nodes.Add(serializedNode);
        }

        // 序列化连线（用端口属性名引用）
        foreach (var conn in graph.Connections)
        {
            var sourceNode = graph.GetNode(conn.SourceNodeId);
            var targetNode = graph.GetNode(conn.TargetNodeId);
            if (sourceNode == null || targetNode == null) continue;

            var sourcePort = sourceNode.GetPort(conn.SourcePortId);
            var targetPort = targetNode.GetPort(conn.TargetPortId);
            if (sourcePort == null || targetPort == null) continue;

            var sourcePortName = sourceNode.GetPortName(sourcePort);
            var targetPortName = targetNode.GetPortName(targetPort);
            if (sourcePortName == null || targetPortName == null) continue;

            result.Connections.Add(new SerializedConnection
            {
                SourceNodeId = conn.SourceNodeId,
                SourcePortName = sourcePortName,
                TargetNodeId = conn.TargetNodeId,
                TargetPortName = targetPortName
            });
        }

        return result;
    }

    /// <summary>SerializedGraph DTO → NodeGraph（重建节点和连线）</summary>
    private static NodeGraph FromSerializedGraph(SerializedGraph data)
    {
        var graph = new NodeGraph();

        // 第一阶段：重建所有节点
        foreach (var sn in data.Nodes)
        {
            var type = Type.GetType(sn.TypeName);
            if (type == null)
                throw new InvalidOperationException($"无法加载节点类型: {sn.TypeName}");

            var node = Activator.CreateInstance(type) as NodeBase;
            if (node == null)
                throw new InvalidOperationException($"无法创建节点实例: {sn.TypeName}");

            // 恢复 ID（保持连线引用一致）
            node.Id = sn.Id;
            node.X = sn.X;
            node.Y = sn.Y;

            // 恢复属性值
            foreach (var (name, value) in sn.Properties)
            {
                var converted = DeserializeValue(value);
                node.SetPropertyValue(name, converted);
            }

            graph.AddNode(node);
        }

        // 第二阶段：重建连线
        foreach (var sc in data.Connections)
        {
            var sourceNode = graph.GetNode(sc.SourceNodeId);
            var targetNode = graph.GetNode(sc.TargetNodeId);
            if (sourceNode == null || targetNode == null) continue;

            var sourcePort = sourceNode.GetPortByName(sc.SourcePortName);
            var targetPort = targetNode.GetPortByName(sc.TargetPortName);
            if (sourcePort == null || targetPort == null) continue;

            graph.TryConnect(sourceNode.Id, sourcePort.Id, targetNode.Id, targetPort.Id);
        }

        return graph;
    }

    /// <summary>序列化单个属性值（枚举转为字符串）</summary>
    private static object? SerializeValue(object? value)
    {
        if (value == null) return null;
        if (value is Enum) return value.ToString();
        return value;
    }

    /// <summary>反序列化属性值（处理 JsonElement 类型转换）</summary>
    private static object? DeserializeValue(object? value)
    {
        if (value == null) return null;
        if (value is JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Number => el.TryGetInt64(out var l) ? (object)l : el.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => el.GetRawText()
            };
        }

        return value;
    }
}
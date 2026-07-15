using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NodeEditor.Core.Attributes;
using NodeEditor.Core.Models;

namespace NodeEditor.Core.Discovery;

/// <summary>
/// 节点发现器 — 通过反射扫描程序集，自动发现所有自定义节点类型。
/// 开发者只需将包含节点定义的程序集传入 ScanAssembly 即可。
/// </summary>
public class NodeDiscovery
{
    /// <summary>已发现的节点类型描述（TypeId → Descriptor）</summary>
    public Dictionary<string, NodeDescriptor> Descriptors { get; } = new();

    /// <summary>
    /// 扫描程序集中所有标记了 [Node] 特性且继承自 NodeBase 的类型
    /// </summary>
    public void ScanAssembly(Assembly assembly)
    {
        var nodeTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(NodeBase).IsAssignableFrom(t) &&
                        t.GetCustomAttribute<NodeAttribute>() != null);

        foreach (var type in nodeTypes)
        {
            var descriptor = BuildDescriptor(type);
            Descriptors[descriptor.TypeId] = descriptor;
        }
    }

    /// <summary>扫描多个程序集</summary>
    public void ScanAssemblies(IEnumerable<Assembly> assemblies)
    {
        foreach (var assembly in assemblies)
            ScanAssembly(assembly);
    }

    /// <summary>根据 TypeId 创建节点实例</summary>
    public NodeBase? CreateNode(string typeId)
    {
        if (!Descriptors.TryGetValue(typeId, out var descriptor))
            return null;

        var type = Type.GetType(descriptor.TypeId);
        if (type == null) return null;

        return Activator.CreateInstance(type) as NodeBase;
    }

    /// <summary>获取所有节点描述</summary>
    public List<NodeDescriptor> GetAllDescriptors() => Descriptors.Values.ToList();

    /// <summary>按分类分组返回节点描述</summary>
    public Dictionary<string, List<NodeDescriptor>> GetByCategory()
    {
        return Descriptors.Values
            .GroupBy(d => string.IsNullOrEmpty(d.Category) ? "未分类" : d.Category)
            .ToDictionary(g => g.Key, g => g.OrderBy(d => d.DisplayName).ToList());
    }

    /// <summary>通过反射构建节点类型描述（不实例化节点）</summary>
    private static NodeDescriptor BuildDescriptor(Type type)
    {
        var nodeAttr = type.GetCustomAttribute<NodeAttribute>()!;
        var descriptor = new NodeDescriptor
        {
            TypeId = type.AssemblyQualifiedName!,
            DisplayName = nodeAttr.DisplayName,
            Category = nodeAttr.Category,
            Color = nodeAttr.Color,
            Description = nodeAttr.Description,
            Kind = nodeAttr.Kind
        };

        // 扫描端口属性
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(NodePort)) continue;

            var inputAttr = prop.GetCustomAttribute<InputAttribute>();
            var outputAttr = prop.GetCustomAttribute<OutputAttribute>();
            var execInputAttr = prop.GetCustomAttribute<ExecInputAttribute>();
            var execOutputAttr = prop.GetCustomAttribute<ExecOutputAttribute>();

            if (execInputAttr != null)
            {
                descriptor.InputPorts.Add(new PortDescriptor
                {
                    PropertyName = prop.Name,
                    DisplayName = execInputAttr.DisplayName,
                    PortTypeName = "Exec",
                    AllowMultiple = false,
                    Kind = PortKind.Exec
                });
            }
            else if (execOutputAttr != null)
            {
                descriptor.OutputPorts.Add(new PortDescriptor
                {
                    PropertyName = prop.Name,
                    DisplayName = execOutputAttr.DisplayName,
                    PortTypeName = "Exec",
                    AllowMultiple = true,
                    Kind = PortKind.Exec
                });
            }
            else if (inputAttr != null)
            {
                descriptor.InputPorts.Add(new PortDescriptor
                {
                    PropertyName = prop.Name,
                    DisplayName = inputAttr.DisplayName,
                    PortTypeName = inputAttr.PortType.Name,
                    AllowMultiple = inputAttr.AllowMultiple,
                    Kind = PortKind.Data
                });
            }
            else if (outputAttr != null)
            {
                descriptor.OutputPorts.Add(new PortDescriptor
                {
                    PropertyName = prop.Name,
                    DisplayName = outputAttr.DisplayName,
                    PortTypeName = outputAttr.PortType.Name,
                    AllowMultiple = true,
                    Kind = PortKind.Data
                });
            }
        }

        // 扫描可编辑属性
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propAttr = prop.GetCustomAttribute<NodePropertyAttribute>();
            if (propAttr == null) continue;

            descriptor.Properties.Add(new PropertyDescriptor
            {
                PropertyName = prop.Name,
                DisplayName = propAttr.DisplayName,
                PropertyTypeName = prop.PropertyType.Name,
                Group = propAttr.Group,
                Order = propAttr.Order
            });
        }

        descriptor.Properties = descriptor.Properties.OrderBy(p => p.Order).ToList();
        return descriptor;
    }
}
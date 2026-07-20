using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NodeEditor.Core.Attributes;

namespace NodeEditor.Core.Models;

/// <summary>
/// 所有节点的基类。开发者继承此类并通过 [Node]、[Input]、[Output]、[NodeProperty] 特性定义自定义节点。
/// 基类在构造时自动通过反射扫描特性，完成端口初始化和元数据读取。
/// </summary>
public abstract class NodeBase
{
    private string _id = string.Empty;

    /// <summary>节点唯一 ID（设置时同步更新所有端口的 NodeId）</summary>
    public string Id
    {
        get => _id;
        internal set
        {
            _id = value;
            // 反序列化恢复 ID 时，同步更新端口的 NodeId
            foreach (var port in Ports)
                port.NodeId = value;
        }
    }

    /// <summary>节点在画布上的 X 坐标</summary>
    public double X { get; set; }

    /// <summary>节点在画布上的 Y 坐标</summary>
    public double Y { get; set; }

    /// <summary>节点显示名称（来自 [Node] 特性）</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>节点分类（来自 [Node] 特性）</summary>
    public string Category { get; private set; } = string.Empty;

    /// <summary>节点颜色（来自 [Node] 特性）</summary>
    public string Color { get; private set; } = "#5A5A5A";

    /// <summary>节点描述（来自 [Node] 特性）</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>节点种类（来自 [Node] 特性）</summary>
    public NodeKind Kind { get; private set; } = NodeKind.Get;

    /// <summary>事件节点执行顺序（来自 [Node] 特性，值越小越先执行）</summary>
    public int ExecutionOrder { get; private set; }

    /// <summary>节点类型全名（用于序列化时的类型恢复）</summary>
    public string TypeName => GetType().AssemblyQualifiedName!;

    /// <summary>该节点的所有端口</summary>
    public List<NodePort> Ports { get; } = new();

    // 端口在构造后不再变化，缓存分组结果避免每次访问都做 LINQ 过滤和分配
    private IReadOnlyList<NodePort>? _inputPorts;
    private IReadOnlyList<NodePort>? _outputPorts;

    /// <summary>输入端口列表</summary>
    public IReadOnlyList<NodePort> InputPorts =>
        _inputPorts ??= Ports.Where(p => p.Direction == PortDirection.Input).ToList();

    /// <summary>输出端口列表</summary>
    public IReadOnlyList<NodePort> OutputPorts =>
        _outputPorts ??= Ports.Where(p => p.Direction == PortDirection.Output).ToList();

    /// <summary>端口属性名 → NodePort 的映射（用于序列化时按属性名引用端口）</summary>
    private readonly Dictionary<string, NodePort> _portByName = new();

    /// <summary>可编辑属性名到 PropertyInfo 的映射（用于序列化和编辑器绑定）</summary>
    private readonly Dictionary<string, PropertyInfo> _editableProperties = new();

    protected NodeBase()
    {
        Id = Guid.NewGuid().ToString("N");
        LoadMetadata();
        InitializePorts();
        RegisterEditableProperties();
    }

    /// <summary>
    /// 读取 [Node] 特性元数据
    /// </summary>
    private void LoadMetadata()
    {
        var attr = GetType().GetCustomAttribute<NodeAttribute>();
        if (attr != null)
        {
            DisplayName = attr.DisplayName;
            Category = attr.Category;
            Color = attr.Color;
            Description = attr.Description;
            Kind = attr.Kind;
            ExecutionOrder = attr.ExecutionOrder;
        }
        else
        {
            DisplayName = GetType().Name;
        }
    }

    /// <summary>
    /// 通过反射扫描 [Input] / [Output] 特性，自动创建端口并赋值到对应属性
    /// </summary>
    private void InitializePorts()
    {
        var properties = GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(NodePort) && p.CanWrite);

        foreach (var prop in properties)
        {
            var inputAttr = prop.GetCustomAttribute<InputAttribute>();
            var outputAttr = prop.GetCustomAttribute<OutputAttribute>();
            var execInputAttr = prop.GetCustomAttribute<ExecInputAttribute>();
            var execOutputAttr = prop.GetCustomAttribute<ExecOutputAttribute>();

            NodePort? port = null;
            if (execInputAttr != null)
            {
                port = new NodePort(execInputAttr.DisplayName, typeof(void), PortDirection.Input, false, PortKind.Exec);
            }
            else if (execOutputAttr != null)
            {
                port = new NodePort(execOutputAttr.DisplayName, typeof(void), PortDirection.Output, false, PortKind.Exec);
            }
            else if (inputAttr != null)
            {
                port = new NodePort(inputAttr.DisplayName, inputAttr.PortType, PortDirection.Input, inputAttr.AllowMultiple, PortKind.Data);
            }
            else if (outputAttr != null)
            {
                port = new NodePort(outputAttr.DisplayName, outputAttr.PortType, PortDirection.Output, false, PortKind.Data);
            }

            if (port != null)
            {
                port.NodeId = Id;
                Ports.Add(port);
                _portByName[prop.Name] = port;
                prop.SetValue(this, port);
            }
        }
    }

    /// <summary>
    /// 注册标记了 [NodeProperty] 的属性，供编辑器编辑和序列化使用
    /// </summary>
    private void RegisterEditableProperties()
    {
        var properties = GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<NodePropertyAttribute>() != null && p.CanRead && p.CanWrite);

        foreach (var prop in properties)
        {
            _editableProperties[prop.Name] = prop;
        }
    }

    /// <summary>根据端口 ID 获取端口</summary>
    public NodePort? GetPort(string portId) => Ports.FirstOrDefault(p => p.Id == portId);

    /// <summary>根据属性名获取端口</summary>
    public NodePort? GetPortByName(string propertyName) =>
        _portByName.TryGetValue(propertyName, out var port) ? port : null;

    /// <summary>获取端口的属性名（反向查找）</summary>
    public string? GetPortName(NodePort port) =>
        _portByName.FirstOrDefault(kv => kv.Value == port).Key;

    /// <summary>
    /// 获取所有可编辑属性的信息（属性名、显示名、类型、分组、当前值）
    /// </summary>
    public List<EditablePropertyInfo> GetEditableProperties()
    {
        var result = new List<EditablePropertyInfo>();
        foreach (var (name, prop) in _editableProperties)
        {
            var attr = prop.GetCustomAttribute<NodePropertyAttribute>()!;
            result.Add(new EditablePropertyInfo(
                PropertyName: name,
                DisplayName: attr.DisplayName,
                PropertyType: prop.PropertyType,
                Group: attr.Group,
                Order: attr.Order,
                CurrentValue: prop.GetValue(this)
            ));
        }

        return result.OrderBy(p => p.Order).ToList();
    }

    /// <summary>设置可编辑属性的值</summary>
    public void SetPropertyValue(string propertyName, object? value)
    {
        if (_editableProperties.TryGetValue(propertyName, out var prop))
        {
            var converted = ConvertValue(value, prop.PropertyType);
            prop.SetValue(this, converted);
        }
    }

    /// <summary>获取可编辑属性的值</summary>
    public object? GetPropertyValue(string propertyName)
    {
        if (_editableProperties.TryGetValue(propertyName, out var prop))
        {
            return prop.GetValue(this);
        }

        return null;
    }

    /// <summary>
    /// 获取所有可编辑属性的名称→值字典（用于序列化）
    /// </summary>
    public Dictionary<string, object?> GetPropertyValues()
    {
        var dict = new Dictionary<string, object?>();
        foreach (var (name, prop) in _editableProperties)
        {
            dict[name] = prop.GetValue(this);
        }

        return dict;
    }

    /// <summary>
    /// 批量设置可编辑属性值（用于反序列化）
    /// </summary>
    public void SetPropertyValues(Dictionary<string, object?> values)
    {
        if (values == null) return;
        foreach (var (name, value) in values)
        {
            SetPropertyValue(name, value);
        }
    }

    /// <summary>
    /// 节点执行逻辑 — 子类重写以实现具体行为。
    /// 从输入端口读取数据，处理后写入输出端口。
    /// </summary>
    public virtual void Execute()
    {
        // 默认空实现，纯数据节点无需重写
    }

    /// <summary>
    /// 获取输入端口的值：已连接则返回上游输出端口的值，否则返回端口本地值。
    /// 泛型辅助方法，子类可直接使用。
    /// </summary>
    protected T GetInput<T>(NodePort port, T defaultValue = default!)
    {
        object? value = null;
        if (port.IsConnected)
        {
            var connectedPort = port.GetConnectedPort();
            value = connectedPort?.Value;
        }
        else
        {
            value = port.Value;
        }

        if (value is T v) return v;
        // 数值类型转换：boxed int → double 等
        if (value != null)
        {
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
            }
        }

        return defaultValue;
    }

    /// <summary>
    /// 获取当前应该沿哪些执行输出端口继续执行。
    /// 默认返回所有执行输出端口。Branch 等条件节点可重写此方法以实现选择性执行。
    /// </summary>
    public virtual IEnumerable<NodePort> GetActiveExecOutputs()
    {
        return OutputPorts.Where(p => p.Kind == PortKind.Exec);
    }

    /// <summary>
    /// 从指定的执行输出端口出发，递归执行整条执行链（使用独立的 visited 集合）。
    /// 供 For/While 等循环节点在 Execute() 内部调用以执行循环体。
    /// </summary>
    protected void ExecuteExecChain(NodePort execOutput)
    {
        if (execOutput == null) return;
        var visited = new HashSet<string>();
        foreach (var conn in execOutput.Connections)
        {
            if (conn.TargetNode != null)
                ExecuteDownstream(conn.TargetNode, visited);
        }
    }

    /// <summary>递归执行节点及其下游执行链</summary>
    private static void ExecuteDownstream(NodeBase node, HashSet<string> visited)
    {
        if (visited.Contains(node.Id)) return;
        visited.Add(node.Id);
        node.Execute();
        foreach (var port in node.GetActiveExecOutputs())
        {
            foreach (var conn in port.Connections)
            {
                if (conn.TargetNode != null)
                    ExecuteDownstream(conn.TargetNode, visited);
            }
        }
    }

    /// <summary>类型转换辅助方法</summary>
    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null) return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

        var sourceType = value.GetType();
        if (targetType.IsAssignableFrom(sourceType)) return value;

        // 处理 nullable
        var underlying = Nullable.GetUnderlyingType(targetType);
        if (underlying != null) targetType = underlying;

        // 枚举支持字符串转换
        if (targetType.IsEnum)
        {
            return value is string s ? Enum.Parse(targetType, s, true) : Enum.ToObject(targetType, value);
        }

        return Convert.ChangeType(value, targetType);
    }
}

/// <summary>
/// 可编辑属性的描述信息
/// </summary>
public record EditablePropertyInfo(
    string PropertyName,
    string DisplayName,
    Type PropertyType,
    string Group,
    int Order,
    object? CurrentValue
);
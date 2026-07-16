using NodeEditor.Core.Attributes;
using NodeEditor.Core.Models;

namespace NodeEditor.Samples;

// ════════════════════════════════════════════════════════════════════════
//  生命周期节点（Event）
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// 图开始事件 — 图执行时的入口点。
/// 只有一个执行输出端口，连接到第一个要执行的 Action/Task 节点。
/// </summary>
[Node("OnStart", Category = "生命周期", Color = "#E68A00", Description = "图开始时触发", Kind = NodeKind.Event)]
public class OnStartNode : NodeBase
{
    [ExecOutput("开始")] public NodePort ExecOut { get; set; } = null!;

    public override void Execute()
    {
    }
}

/// <summary>
/// 图结束事件 — 在所有 OnStart 链执行完毕后触发。
/// 只有执行输出端口，可连接清理/收尾动作。
/// </summary>
[Node("OnEnd", Category = "生命周期", Color = "#E68A00", Description = "图结束时触发（OnStart 链完成后）", Kind = NodeKind.Event)]
public class OnEndNode : NodeBase
{
    [ExecOutput("结束")] public NodePort ExecOut { get; set; } = null!;

    public override void Execute()
    {
    }
}

// ════════════════════════════════════════════════════════════════════════
//  动作节点（Action）
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// 打印动作 — 接收一个字符串输入并打印到控制台。
/// 有执行输入/输出端口，串联在执行链中。
/// </summary>
[Node("打印", Category = "动作", Color = "#4CAF50", Description = "将输入值打印到控制台（支持任意类型）", Kind = NodeKind.Action)]
public class PrintActionNode : NodeBase
{
    [ExecInput("In")] public NodePort ExecIn { get; set; } = null!;

    [ExecOutput("Out")] public NodePort ExecOut { get; set; } = null!;

    [Input("消息", typeof(object))] public NodePort MessageInput { get; set; } = null!;

    [NodeProperty("前缀", Group = "格式", Order = 0)]
    public string Prefix { get; set; } = "";

    public override void Execute()
    {
        var message = GetInputValueString(MessageInput);
        var output = string.IsNullOrEmpty(Prefix) ? message : $"[{Prefix}] {message}";
        ExecutionLogger.Log(output, nameof(PrintActionNode));
    }

    private static string GetInputValueString(NodePort port)
    {
        if (port.IsConnected)
        {
            var connectedPort = port.GetConnectedPort();
            return connectedPort?.Value?.ToString() ?? "";
        }
        return port.Value?.ToString() ?? "";
    }
}

/// <summary>
/// 设置变量动作 — 将一个值赋给指定变量名。
/// </summary>
[Node("设置变量", Category = "动作", Color = "#4CAF50", Description = "将输入值设置到指定变量", Kind = NodeKind.Action)]
public class SetVariableActionNode : NodeBase
{
    [ExecInput("In")] public NodePort ExecIn { get; set; } = null!;

    [ExecOutput("Out")] public NodePort ExecOut { get; set; } = null!;

    [Input("值", typeof(object))] public NodePort ValueInput { get; set; } = null!;

    [NodeProperty("变量名", Group = "变量", Order = 0)]
    public string VariableName { get; set; } = "var1";

    /// <summary>设置后的值（供其他节点读取）</summary>
    public object? StoredValue { get; private set; }

    public override void Execute()
    {
        if (ValueInput.IsConnected)
        {
            var connectedPort = ValueInput.GetConnectedPort();
            StoredValue = connectedPort?.Value;
        }
        else
        {
            StoredValue = ValueInput.Value;
        }

        GraphVariableStore.Set(VariableName, StoredValue);
        ExecutionLogger.Log($"[SetVariable] {VariableName} = {StoredValue}", nameof(SetVariableActionNode));
    }
}

// ════════════════════════════════════════════════════════════════════════
//  数据节点（Get）
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// 获取变量 — 从全局变量存储中读取指定变量的值。
/// 无执行端口，只有数据输出。
/// </summary>
[Node("获取变量", Category = "数据", Color = "#2196F3", Description = "读取指定变量的值", Kind = NodeKind.Get)]
public class GetVariableNode : NodeBase
{
    [NodeProperty("变量名", Group = "变量", Order = 0)]
    public string VariableName { get; set; } = "var1";

    [Output("值", typeof(object))] public NodePort ValueOutput { get; set; } = null!;

    public override void Execute()
    {
        ValueOutput.Value = GraphVariableStore.Get(VariableName);
    }
}

/// <summary>
/// 字符串常量 — 输出一个固定的字符串值。
/// </summary>
[Node("字符串常量", Category = "数据", Color = "#2196F3", Description = "输出一个可编辑的字符串常量", Kind = NodeKind.Get)]
public class StringConstantNode : NodeBase
{
    [NodeProperty("文本", Group = "基础", Order = 0)]
    public string Text { get; set; } = "";

    [Output("值", typeof(string))] public NodePort Output { get; set; } = null!;

    public override void Execute()
    {
        Output.Value = Text;
    }
}

// ════════════════════════════════════════════════════════════════════════
//  全局变量存储（简易实现，供 Set/Get 变量节点使用）
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// 简易全局变量存储 — 在单次图执行期间保存变量值。
/// </summary>
public static class GraphVariableStore
{
    private static readonly System.Collections.Generic.Dictionary<string, object?> _store = new();

    public static void Set(string name, object? value) => _store[name] = value;

    public static object? Get(string name) =>
        _store.TryGetValue(name, out var value) ? value : null;

    public static void Clear() => _store.Clear();
}
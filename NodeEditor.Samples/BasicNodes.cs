using NodeEditor.Core.Attributes;
using NodeEditor.Core.Models;

namespace NodeEditor.Samples;

/// <summary>
/// 常量节点 — 输出一个固定数值，可在线编辑。
/// 常作为数据流的起点。
/// </summary>
[Node("常量值", Category = "数学", Color = "#2196F3", Description = "输出一个可编辑的常量数值", Kind = NodeKind.Get)]
public class ConstantNode : NodeBase
{
    [NodeProperty("数值", Group = "基础", Order = 0)]
    public double Value { get; set; } = 1.0;

    [Output("值", typeof(double))] public NodePort Output { get; set; } = null!;

    public override void Execute()
    {
        Output.Value = Value;
    }
}

/// <summary>
/// 显示节点 — 接收一个值并在执行时输出到控制台。
/// 常作为数据流的终点/调试用。
/// </summary>
[Node("显示输出", Category = "调试", Color = "#FF9800", Description = "将输入值打印到控制台", Kind = NodeKind.Get)]
public class DisplayNode : NodeBase
{
    [Input("输入", typeof(double))] public NodePort Input { get; set; } = null!;

    [NodeProperty("标签", Group = "基础", Order = 0)]
    public string Label { get; set; } = "输出";

    /// <summary>最后一次接收到的值</summary>
    public double LastValue { get; private set; }

    public override void Execute()
    {
        if (Input.IsConnected)
        {
            var connectedPort = Input.GetConnectedPort();
            LastValue = connectedPort?.Value is double d ? d : 0;
        }
        else
        {
            LastValue = Input.Value is double v ? v : 0;
        }

        System.Console.WriteLine($"[{Label}] = {LastValue}");
    }
}

/// <summary>
/// 字符串拼接节点 — 将两个字符串输入拼接后输出。
/// 展示非数值类型的端口用法。
/// </summary>
[Node("字符串拼接", Category = "字符串", Color = "#9C27B0", Description = "将两个字符串拼接为一个", Kind = NodeKind.Get)]
public class StringConcatNode : NodeBase
{
    [NodeProperty("分隔符", Group = "基础", Order = 0)]
    public string Separator { get; set; } = " ";

    [Input("A", typeof(string))] public NodePort InputA { get; set; } = null!;

    [Input("B", typeof(string))] public NodePort InputB { get; set; } = null!;

    [Output("结果", typeof(string))] public NodePort Result { get; set; } = null!;

    public override void Execute()
    {
        var a = GetStringValue(InputA);
        var b = GetStringValue(InputB);
        Result.Value = string.IsNullOrEmpty(Separator) ? a + b : $"{a}{Separator}{b}";
    }

    private static string GetStringValue(NodePort port)
    {
        if (port.IsConnected)
        {
            var connectedPort = port.GetConnectedPort();
            return connectedPort?.Value?.ToString() ?? "";
        }

        return port.Value?.ToString() ?? "";
    }
}
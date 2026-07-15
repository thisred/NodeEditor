using NodeEditor.Core.Attributes;
using NodeEditor.Core.Models;

namespace NodeEditor.Samples;

/// <summary>
/// 数学运算类型枚举
/// </summary>
public enum MathOperation
{
    Add,
    Subtract,
    Multiply,
    Divide
}

/// <summary>
/// 加法/数学运算节点 — 接收两个数值输入，根据运算类型输出结果。
/// 这是开发者自定义节点的典型示例。
/// </summary>
[Node("数学运算", Category = "数学", Color = "#4CAF50", Description = "对两个输入值执行加减乘除运算", Kind = NodeKind.Get)]
public class MathNode : NodeBase
{
    [NodeProperty("运算类型", Group = "运算", Order = 0)]
    public MathOperation Operation { get; set; } = MathOperation.Add;

    [Input("A", typeof(double))] public NodePort InputA { get; set; } = null!;

    [Input("B", typeof(double))] public NodePort InputB { get; set; } = null!;

    [Output("结果", typeof(double))] public NodePort Result { get; set; } = null!;

    public override void Execute()
    {
        var a = GetInputValue(InputA);
        var b = GetInputValue(InputB);

        Result.Value = Operation switch
        {
            MathOperation.Add => a + b,
            MathOperation.Subtract => a - b,
            MathOperation.Multiply => a * b,
            MathOperation.Divide => b != 0 ? a / b : 0,
            _ => 0
        };
    }

    private static double GetInputValue(NodePort port)
    {
        if (port.IsConnected)
        {
            var connectedPort = port.GetConnectedPort();
            return connectedPort?.Value is double d ? d : 0;
        }

        return port.Value is double v ? v : 0;
    }
}
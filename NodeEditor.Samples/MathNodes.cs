using NodeEditor.Core.Attributes;
using NodeEditor.Core.Models;

namespace NodeEditor.Samples;

// ════════════════════════════════════════════════════════════════════════
//  数学补充节点
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// 随机数 — 在 [min, max) 范围内生成随机浮点数。
/// 参考 Unity AbilityGraph 的 RandomInteger 节点。
/// </summary>
[Node("随机数", Category = "数学", Color = "#4CAF50", Description = "生成 [min, max) 范围内的随机浮点数", Kind = NodeKind.Get)]
public class RandomNode : NodeBase
{
    [NodeProperty("最小值", Group = "范围", Order = 0)]
    public double Min { get; set; } = 0.0;

    [NodeProperty("最大值", Group = "范围", Order = 1)]
    public double Max { get; set; } = 1.0;

    [Output("结果", typeof(double))] public NodePort Result { get; set; } = null!;

    private static readonly System.Random s_random = new();

    public override void Execute()
    {
        Result.Value = s_random.NextDouble() * (Max - Min) + Min;
    }
}

/// <summary>
/// 随机整数 — 生成 [min, max) 范围内的随机整数。
/// 输出 int 类型，可通过数值拓宽连接到 double 输入端口。
/// </summary>
[Node("随机整数", Category = "数学", Color = "#4CAF50", Description = "生成 [min, max) 范围内的随机整数", Kind = NodeKind.Get)]
public class RandomIntNode : NodeBase
{
    [NodeProperty("最小值", Group = "范围", Order = 0)]
    public int Min { get; set; } = 0;

    [NodeProperty("最大值", Group = "范围", Order = 1)]
    public int Max { get; set; } = 100;

    [Output("结果", typeof(int))] public NodePort Result { get; set; } = null!;

    private static readonly System.Random s_random = new();

    public override void Execute()
    {
        Result.Value = s_random.Next(Min, Max);
    }
}

/// <summary>
/// 数值钳制 — 将输入值限制在 [min, max] 范围内。
/// </summary>
[Node("钳制 (Clamp)", Category = "数学", Color = "#4CAF50", Description = "将值限制在 [min, max] 范围内", Kind = NodeKind.Get)]
public class ClampNode : NodeBase
{
    [Input("值", typeof(double))] public NodePort ValueInput { get; set; } = null!;
    [Input("最小值", typeof(double))] public NodePort MinInput { get; set; } = null!;
    [Input("最大值", typeof(double))] public NodePort MaxInput { get; set; } = null!;
    [Output("结果", typeof(double))] public NodePort Result { get; set; } = null!;

    public override void Execute()
    {
        var value = GetInput(ValueInput, 0.0);
        var min = GetInput(MinInput, 0.0);
        var max = GetInput(MaxInput, double.MaxValue);
        Result.Value = System.Math.Max(min, System.Math.Min(max, value));
    }
}

/// <summary>
/// 绝对值 — 返回输入值的绝对值。
/// </summary>
[Node("绝对值 (Abs)", Category = "数学", Color = "#4CAF50", Description = "返回输入值的绝对值", Kind = NodeKind.Get)]
public class AbsNode : NodeBase
{
    [Input("值", typeof(double))] public NodePort ValueInput { get; set; } = null!;
    [Output("结果", typeof(double))] public NodePort Result { get; set; } = null!;

    public override void Execute()
    {
        var value = GetInput(ValueInput, 0.0);
        Result.Value = System.Math.Abs(value);
    }
}

/// <summary>
/// 取最小值 — 返回两个输入值中较小的一个。
/// </summary>
[Node("最小值 (Min)", Category = "数学", Color = "#4CAF50", Description = "返回两个值中较小的值", Kind = NodeKind.Get)]
public class MinNode : NodeBase
{
    [Input("A", typeof(double))] public NodePort InputA { get; set; } = null!;
    [Input("B", typeof(double))] public NodePort InputB { get; set; } = null!;
    [Output("结果", typeof(double))] public NodePort Result { get; set; } = null!;

    public override void Execute()
    {
        var a = GetInput(InputA, 0.0);
        var b = GetInput(InputB, 0.0);
        Result.Value = System.Math.Min(a, b);
    }
}

/// <summary>
/// 取最大值 — 返回两个输入值中较大的一个。
/// </summary>
[Node("最大值 (Max)", Category = "数学", Color = "#4CAF50", Description = "返回两个值中较大的值", Kind = NodeKind.Get)]
public class MaxNode : NodeBase
{
    [Input("A", typeof(double))] public NodePort InputA { get; set; } = null!;
    [Input("B", typeof(double))] public NodePort InputB { get; set; } = null!;
    [Output("结果", typeof(double))] public NodePort Result { get; set; } = null!;

    public override void Execute()
    {
        var a = GetInput(InputA, 0.0);
        var b = GetInput(InputB, 0.0);
        Result.Value = System.Math.Max(a, b);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  布尔常量节点
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// 布尔常量 — 输出一个固定的 true/false 值。
/// 常用于逻辑运算的固定输入。
/// </summary>
[Node("布尔常量", Category = "数据", Color = "#2196F3", Description = "输出一个布尔常量值", Kind = NodeKind.Get)]
public class BooleanConstantNode : NodeBase
{
    [NodeProperty("值", Group = "基础", Order = 0)]
    public bool Value { get; set; } = true;

    [Output("值", typeof(bool))] public NodePort Output { get; set; } = null!;

    public override void Execute()
    {
        Output.Value = Value;
    }
}

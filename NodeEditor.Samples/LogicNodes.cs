using NodeEditor.Core.Attributes;
using NodeEditor.Core.Models;

namespace NodeEditor.Samples;

// ════════════════════════════════════════════════════════════════════════
//  比较函数枚举
// ════════════════════════════════════════════════════════════════════════

/// <summary>比较运算类型 — 用于 Comparison 节点选择比较方式</summary>
public enum CompareFunction
{
    Less,
    LessEqual,
    Equal,
    NotEqual,
    Greater,
    GreaterEqual
}

// ════════════════════════════════════════════════════════════════════════
//  布尔逻辑节点
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// 逻辑与 — 两个布尔输入都为 true 时输出 true。
/// </summary>
[Node("逻辑与 (And)", Category = "逻辑", Color = "#E91E63", Description = "A && B", Kind = NodeKind.Get)]
public class AndNode : NodeBase
{
    [Input("A", typeof(bool))] public NodePort InputA { get; set; } = null!;
    [Input("B", typeof(bool))] public NodePort InputB { get; set; } = null!;
    [Output("结果", typeof(bool))] public NodePort Result { get; set; } = null!;

    public override void Execute()
    {
        var a = GetInput(InputA, false);
        var b = GetInput(InputB, false);
        Result.Value = a && b;
    }
}

/// <summary>
/// 逻辑或 — 两个布尔输入任一为 true 时输出 true。
/// </summary>
[Node("逻辑或 (Or)", Category = "逻辑", Color = "#E91E63", Description = "A || B", Kind = NodeKind.Get)]
public class OrNode : NodeBase
{
    [Input("A", typeof(bool))] public NodePort InputA { get; set; } = null!;
    [Input("B", typeof(bool))] public NodePort InputB { get; set; } = null!;
    [Output("结果", typeof(bool))] public NodePort Result { get; set; } = null!;

    public override void Execute()
    {
        var a = GetInput(InputA, false);
        var b = GetInput(InputB, false);
        Result.Value = a || b;
    }
}

/// <summary>
/// 逻辑非 — 布尔取反。
/// </summary>
[Node("逻辑非 (Not)", Category = "逻辑", Color = "#E91E63", Description = "!A", Kind = NodeKind.Get)]
public class NotNode : NodeBase
{
    [Input("输入", typeof(bool))] public NodePort Input { get; set; } = null!;
    [Output("结果", typeof(bool))] public NodePort Result { get; set; } = null!;

    public override void Execute()
    {
        var a = GetInput(Input, false);
        Result.Value = !a;
    }
}

// ════════════════════════════════════════════════════════════════════════
//  比较节点
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// 数值比较 — 根据比较类型（大于/小于/等于等）比较两个数值，输出布尔结果。
/// 参考 Unity AbilityGraph 的 Comparison 节点设计。
/// </summary>
[Node("数值比较", Category = "逻辑", Color = "#E91E63", Description = "比较两个数值（大于/小于/等于等）", Kind = NodeKind.Get)]
public class ComparisonNode : NodeBase
{
    [NodeProperty("比较方式", Group = "运算", Order = 0)]
    public CompareFunction CompareFunction { get; set; } = CompareFunction.GreaterEqual;

    [Input("A", typeof(double))] public NodePort InputA { get; set; } = null!;
    [Input("B", typeof(double))] public NodePort InputB { get; set; } = null!;
    [Output("结果", typeof(bool))] public NodePort Result { get; set; } = null!;

    public override void Execute()
    {
        var a = GetInput(InputA, 0.0);
        var b = GetInput(InputB, 0.0);
        Result.Value = CompareFunction switch
        {
            CompareFunction.Less          => a < b,
            CompareFunction.LessEqual      => a <= b,
            CompareFunction.Equal         => System.Math.Abs(a - b) < 0.0001,
            CompareFunction.NotEqual      => System.Math.Abs(a - b) >= 0.0001,
            CompareFunction.Greater        => a > b,
            CompareFunction.GreaterEqual   => a >= b,
            _ => false
        };
    }
}

/// <summary>
/// 字符串相等 — 判断两个字符串是否相等。
/// </summary>
[Node("字符串相等", Category = "逻辑", Color = "#E91E63", Description = "判断两个字符串是否相等", Kind = NodeKind.Get)]
public class StringEqualNode : NodeBase
{
    [Input("A", typeof(string))] public NodePort InputA { get; set; } = null!;
    [Input("B", typeof(string))] public NodePort InputB { get; set; } = null!;
    [Output("结果", typeof(bool))] public NodePort Result { get; set; } = null!;

    public override void Execute()
    {
        var a = GetInput(InputA, "");
        var b = GetInput(InputB, "");
        Result.Value = string.Equals(a, b, System.StringComparison.Ordinal);
    }
}

using System.Collections.Generic;
using System.Linq;
using NodeEditor.Core.Attributes;
using NodeEditor.Core.Models;

namespace NodeEditor.Samples;

// ════════════════════════════════════════════════════════════════════════
//  流程控制节点
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// 条件分支 — 根据布尔条件选择执行 True 或 False 分支。
/// 只会沿条件匹配的执行输出端口继续执行，另一条分支被跳过。
/// 重写 GetActiveExecOutputs 实现选择性执行。
/// </summary>
[Node("条件分支 (If)", Category = "流程控制", Color = "#9C27B0", Description = "根据条件选择执行 True 或 False 分支", Kind = NodeKind.Action)]
public class BranchNode : NodeBase
{
    [ExecInput("In")] public NodePort ExecIn { get; set; } = null!;
    [ExecOutput("True")] public NodePort ExecTrue { get; set; } = null!;
    [ExecOutput("False")] public NodePort ExecFalse { get; set; } = null!;

    [Input("条件", typeof(bool))] public NodePort ConditionInput { get; set; } = null!;

    /// <summary>当前条件结果（Execute 后更新）</summary>
    private bool _conditionResult;

    public override void Execute()
    {
        _conditionResult = GetInput(ConditionInput, false);
        ExecutionLogger.Log($"[Branch] 条件={_conditionResult} → 走 {_conditionResult} 分支", nameof(BranchNode));
    }

    /// <summary>
    /// 重写：只返回条件匹配的执行输出端口。
    /// True 分支返回 ExecTrue，False 分支返回 ExecFalse。
    /// </summary>
    public override IEnumerable<NodePort> GetActiveExecOutputs()
    {
        return _conditionResult
            ? new[] { ExecTrue }
            : new[] { ExecFalse };
    }
}

/// <summary>
/// 延迟执行 — 等待指定毫秒后继续执行。
/// 简化版：在当前执行线程中同步等待（适用于演示）。
/// 实际项目中可扩展为异步等待（参考 Unity WaitDelay 节点）。
/// </summary>
[Node("延迟 (Delay)", Category = "流程控制", Color = "#9C27B0", Description = "等待指定毫秒后继续执行", Kind = NodeKind.Action)]
public class DelayNode : NodeBase
{
    [ExecInput("In")] public NodePort ExecIn { get; set; } = null!;
    [ExecOutput("Out")] public NodePort ExecOut { get; set; } = null!;

    [Input("毫秒", typeof(double))] public NodePort MillisecondsInput { get; set; } = null!;

    [NodeProperty("毫秒", Group = "延迟", Order = 0)]
    public double Milliseconds { get; set; } = 1000;

    public override void Execute()
    {
        var ms = GetInput(MillisecondsInput, Milliseconds);
        if (ms > 0)
            System.Threading.Thread.Sleep((int)ms);
        ExecutionLogger.Log($"[Delay] 等待 {ms:F0}ms 完成", nameof(DelayNode));
    }
}

/// <summary>
/// For 循环 — 重复执行循环体 N 次，每次迭代输出当前索引。
/// 循环体通过“循环体”执行输出端口连接，执行完毕后沿“完成”端口继续。
/// 在 Execute() 内部通过 ExecuteExecChain 主动执行循环体，
/// GetActiveExecOutputs 只返回“完成”端口，避免图引擎重复遍历循环体。
/// </summary>
[Node("For 循环", Category = "流程控制", Color = "#9C27B0", Description = "重复执行循环体 N 次", Kind = NodeKind.Action)]
public class ForLoopNode : NodeBase
{
    [ExecInput("In")] public NodePort ExecIn { get; set; } = null!;
    [ExecOutput("循环体")] public NodePort ExecLoopBody { get; set; } = null!;
    [ExecOutput("完成")] public NodePort ExecCompleted { get; set; } = null!;

    [Input("次数", typeof(int))] public NodePort CountInput { get; set; } = null!;

    [Output("索引", typeof(int))] public NodePort IndexOutput { get; set; } = null!;

    [NodeProperty("次数", Group = "循环", Order = 0)]
    public int Count { get; set; } = 3;

    /// <summary>当前是否已完成循环（控制 GetActiveExecOutputs 返回“完成”）</summary>
    private bool _completed;

    public override void Execute()
    {
        var count = GetInput(CountInput, Count);
        if (count < 0) count = 0;

        for (int i = 0; i < count; i++)
        {
            IndexOutput.Value = i;
            ExecutionLogger.Log($"[For] 迭代 {i + 1}/{count}", nameof(ForLoopNode));
            // 主动执行循环体执行链（独立 visited 集合，每次迭代重新执行）
            ExecuteExecChain(ExecLoopBody);
        }

        _completed = true;
        ExecutionLogger.Log($"[For] 循环结束，共 {count} 次", nameof(ForLoopNode));
    }

    /// <summary>
    /// 重写：循环体已在 Execute() 内部执行完毕，只返回“完成”端口。
    /// </summary>
    public override IEnumerable<NodePort> GetActiveExecOutputs()
    {
        return _completed ? new[] { ExecCompleted } : System.Array.Empty<NodePort>();
    }
}

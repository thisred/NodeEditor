namespace NodeEditor.Core.Models;

/// <summary>
/// 节点种类 — 决定节点在图中的角色和执行方式。
/// </summary>
public enum NodeKind
{
    /// <summary>事件节点 — 图的生命周期入口/出口（OnStart, OnEnd 等），只有执行输出</summary>
    Event,

    /// <summary>动作节点 — 有执行输入/输出，执行一个动作（打印、设置变量等）</summary>
    Action,

    /// <summary>任务节点 — 有执行输入/输出，执行一个（可能耗时的）任务</summary>
    Task,

    /// <summary>数据节点 — 无执行端口，纯数据获取/处理，按拓扑顺序执行</summary>
    Get
}
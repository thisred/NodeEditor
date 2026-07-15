namespace NodeEditor.Core.Models;

/// <summary>
/// 端口种类：数据端口或执行端口。
/// 数据端口传递值，执行端口传递控制流（执行脉冲）。
/// </summary>
public enum PortKind
{
    /// <summary>数据端口 — 传递具体类型的值</summary>
    Data,

    /// <summary>执行端口 — 传递控制流（执行顺序），不携带数据</summary>
    Exec
}
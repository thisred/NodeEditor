using System;
using NodeEditor.Core.Models;

namespace NodeEditor.Core.Attributes;

/// <summary>
/// 标记一个 NodePort 属性为执行输入端口（控制流入口）。
/// 执行端口不携带数据类型，仅用于控制执行顺序。
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class ExecInputAttribute : Attribute
{
    /// <summary>端口显示名称</summary>
    public string DisplayName { get; }

    public ExecInputAttribute(string displayName)
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }
}
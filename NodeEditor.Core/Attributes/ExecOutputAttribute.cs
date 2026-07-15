using System;
using NodeEditor.Core.Models;

namespace NodeEditor.Core.Attributes;

/// <summary>
/// 标记一个 NodePort 属性为执行输出端口（控制流出口）。
/// 执行端口不携带数据类型，仅用于控制执行顺序。
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class ExecOutputAttribute : Attribute
{
    /// <summary>端口显示名称</summary>
    public string DisplayName { get; }

    public ExecOutputAttribute(string displayName)
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }
}
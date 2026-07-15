using System;

namespace NodeEditor.Core.Attributes;

/// <summary>
/// 标记节点上的可编辑属性（非端口属性）。
/// 编辑器会自动为这些属性生成编辑控件（文本框、下拉框、复选框等）。
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class NodePropertyAttribute : Attribute
{
    /// <summary>属性显示名称</summary>
    public string DisplayName { get; }

    /// <summary>属性分组（用于编辑器面板分组显示）</summary>
    public string Group { get; set; } = "基础";

    /// <summary>排序权重（越小越靠前）</summary>
    public int Order { get; set; } = 0;

    public NodePropertyAttribute(string displayName)
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }
}
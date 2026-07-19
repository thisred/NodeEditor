using System;
using NodeEditor.Core.Models;

namespace NodeEditor.Core.Attributes;

/// <summary>
/// 标记一个类为可被节点编辑器识别的节点。
/// 开发者继承 NodeBase 并打上此特性即可定义自定义节点。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class NodeAttribute : Attribute
{
    /// <summary>在编辑器节点面板中显示的名称</summary>
    public string DisplayName { get; }

    /// <summary>节点分类路径，用 '/' 分隔，如 "数学/运算"</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>节点头部颜色（CSS 十六进制），如 "#4CAF50"</summary>
    public string Color { get; set; } = "#5A5A5A";

    /// <summary>节点描述/提示</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>节点种类（默认 Get，纯数据节点）</summary>
    public NodeKind Kind { get; set; } = NodeKind.Get;

    /// <summary>
    /// 事件节点的执行顺序（仅对 Kind = Event 生效，值越小越先执行）。
    /// 例如 OnStart 用默认 0，OnEnd 用较大的值以保证在所有 OnStart 链之后执行。
    /// </summary>
    public int ExecutionOrder { get; set; }

    public NodeAttribute(string displayName)
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditor.Core.Models;

namespace NodeEditor.Wpf.ViewModels;

/// <summary>
/// 可编辑属性的视图模型 — 供属性面板数据绑定。
/// 支持文本输入和枚举下拉选择。
/// </summary>
public class NodePropertyViewModel : ViewModelBase
{
    private readonly NodeBase _node;

    public string PropertyName { get; }
    public string DisplayName { get; }
    public Type PropertyType { get; }
    public string Group { get; }
    public int Order { get; }

    /// <summary>是否是枚举类型</summary>
    public bool IsEnum => PropertyType.IsEnum;

    /// <summary>如果是枚举，返回所有可选值</summary>
    public List<string>? EnumValues { get; }

    /// <summary>是否是布尔类型</summary>
    public bool IsBool => PropertyType == typeof(bool);

    /// <summary>当前值（字符串表示，用于文本框绑定）</summary>
    public string ValueText
    {
        get
        {
            var val = _node.GetPropertyValue(PropertyName);
            if (val == null) return "";
            if (IsEnum) return val.ToString() ?? "";
            if (IsBool) return val.ToString() ?? "";
            return val.ToString() ?? "";
        }
        set
        {
            if (IsEnum)
            {
                _node.SetPropertyValue(PropertyName, value);
            }
            else if (IsBool)
            {
                _node.SetPropertyValue(PropertyName, value.Equals("True", StringComparison.OrdinalIgnoreCase));
            }
            else if (PropertyType == typeof(double) || PropertyType == typeof(float))
            {
                if (double.TryParse(value, out var d))
                    _node.SetPropertyValue(PropertyName, d);
            }
            else if (PropertyType == typeof(int))
            {
                if (int.TryParse(value, out var i))
                    _node.SetPropertyValue(PropertyName, i);
            }
            else
            {
                _node.SetPropertyValue(PropertyName, value);
            }

            OnPropertyChanged();
        }
    }

    /// <summary>布尔值（用于复选框绑定）</summary>
    public bool BoolValue
    {
        get => _node.GetPropertyValue(PropertyName) is bool b && b;
        set
        {
            _node.SetPropertyValue(PropertyName, value);
            OnPropertyChanged();
        }
    }

    public NodePropertyViewModel(NodeBase node, EditablePropertyInfo info)
    {
        _node = node;
        PropertyName = info.PropertyName;
        DisplayName = info.DisplayName;
        PropertyType = info.PropertyType;
        Group = info.Group;
        Order = info.Order;

        if (PropertyType.IsEnum)
        {
            EnumValues = Enum.GetNames(PropertyType).ToList();
        }
    }
}
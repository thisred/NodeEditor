using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditor.Core.Models;

namespace NodeEditor.ViewModels;

/// <summary>
/// 可编辑属性的视图模型 — 供属性面板数据绑定。
/// </summary>
public class NodePropertyViewModel : ViewModelBase
{
    private readonly NodeBase _node;

    public string PropertyName { get; }
    public string DisplayName { get; }
    public Type PropertyType { get; }
    public string Group { get; }
    public int Order { get; }

    public bool IsEnum => PropertyType.IsEnum;
    public List<string>? EnumValues { get; }
    public bool IsBool => PropertyType == typeof(bool);

    public string ValueText
    {
        get
        {
            var val = _node.GetPropertyValue(PropertyName);
            return val?.ToString() ?? "";
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
            EnumValues = Enum.GetNames(PropertyType).ToList();
    }
}

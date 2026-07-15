using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NodeEditor.Core.Discovery;

namespace NodeEditor.Wpf;

/// <summary>
/// 节点创建弹窗 — 分类浏览 + 全局搜索。
/// 空搜索时显示分类列表；输入文字时搜索所有节点。
/// </summary>
public partial class NodeSearchWindow : Window
{
    private readonly Dictionary<string, List<NodeEntry>> _byCategory;
    private readonly List<NodeEntry> _allNodes;
    private readonly Action<string>? _onSelected;
    private string? _selectedCategory;

    public NodeSearchWindow(IEnumerable<NodeDescriptor> descriptors, Action<string>? onSelected)
    {
        InitializeComponent();
        _onSelected = onSelected;

        var entries = descriptors
            .OrderBy(d => d.DisplayName)
            .Select(d => new NodeEntry(d))
            .ToList();

        _allNodes = entries;
        _byCategory = entries
            .GroupBy(e => string.IsNullOrEmpty(e.Descriptor.Category) ? "未分类" : e.Descriptor.Category)
            .ToDictionary(g => g.Key, g => g.ToList());

        ShowCategories();
        Loaded += (_, _) => SearchBox.Focus();
    }

    // ════════════════ 分类视图 ════════════════

    private void ShowCategories()
    {
        _selectedCategory = null;
        TitleText.Text = "选择分类";
        BackButton.Visibility = Visibility.Collapsed;
        CategoryList.SelectedIndex = -1;
        CategoryList.Visibility = Visibility.Visible;
        NodeList.Visibility = Visibility.Collapsed;
        SearchBox.Text = "";
        FilterView();
        CategoryList.Focus();
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedItem is string category)
        {
            EnterCategory(category);
        }
    }

    private void CategoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CategoryList.SelectedItem is string category)
        {
            EnterCategory(category);
        }
    }

    private void EnterCategory(string category)
    {
        _selectedCategory = category;
        TitleText.Text = category;
        BackButton.Visibility = Visibility.Visible;
        CategoryList.SelectedIndex = -1;
        CategoryList.Visibility = Visibility.Collapsed;
        NodeList.Visibility = Visibility.Visible;
        SearchBox.Text = "";
        FilterView();
        NodeList.Focus();
    }

    // ════════════════ 节点视图 ════════════════

    private void NodeList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        CreateSelected();
    }

    /// <summary>单击创建节点（Preview 事件，在视觉树变化前触发）</summary>
    private void NodeList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? dep = e.OriginalSource as DependencyObject;
        while (dep != null && dep is not ListBoxItem)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is ListBoxItem item && item.DataContext is NodeEntry entry)
        {
            e.Handled = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _onSelected?.Invoke(entry.Descriptor.TypeId);
                Close();
            }));
        }
    }

    private void CreateSelected()
    {
        NodeEntry? entry = NodeList.SelectedItem as NodeEntry;
        if (entry == null && NodeList.Items.Count > 0)
            entry = NodeList.Items[0] as NodeEntry;

        if (entry != null)
        {
            _onSelected?.Invoke(entry.Descriptor.TypeId);
            Close();
        }
    }

    private void BackButton_Click(object sender, MouseButtonEventArgs e)
    {
        ShowCategories();
    }

    // ════════════════ 搜索过滤 ════════════════

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterView();
    }

    private void FilterView()
    {
        var text = SearchBox.Text;

        if (!string.IsNullOrEmpty(text))
        {
            // 有搜索文字 → 搜索所有节点，隐藏分类列表
            CategoryList.Visibility = Visibility.Collapsed;
            NodeList.Visibility = Visibility.Visible;
            var nodes = _allNodes
                .Where(n => n.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase))
                .ToList();
            NodeList.ItemsSource = nodes;
            StatusText.Text = nodes.Count > 0
                ? $"{nodes.Count} 个节点 · 点击创建 · Esc 取消"
                : "无匹配节点";
            return;
        }

        // 空搜索 → 恢复当前层级视图
        if (_selectedCategory == null)
        {
            // 分类视图
            CategoryList.Visibility = Visibility.Visible;
            NodeList.Visibility = Visibility.Collapsed;
            var categories = _byCategory.Keys
                .OrderBy(k => k)
                .ToList();
            CategoryList.ItemsSource = categories;
            StatusText.Text = categories.Count > 0
                ? $"{categories.Count} 个分类 · 点击进入 · Esc 取消"
                : "无分类";
        }
        else
        {
            // 分类内节点视图
            CategoryList.Visibility = Visibility.Collapsed;
            NodeList.Visibility = Visibility.Visible;
            var nodes = _byCategory[_selectedCategory];
            NodeList.ItemsSource = nodes;
            StatusText.Text = nodes.Count > 0
                ? $"{nodes.Count} 个节点 · 点击创建 · Backspace 返回 · Esc 取消"
                : "无节点";
        }
    }

    // ════════════════ 键盘交互 ════════════════

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (NodeList.Visibility == Visibility.Visible)
                    CreateSelected();
                else if (CategoryList.SelectedItem is string cat)
                    EnterCategory(cat);
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Back:
                if (_selectedCategory != null && string.IsNullOrEmpty(SearchBox.Text))
                {
                    ShowCategories();
                    e.Handled = true;
                }

                break;
        }
    }
}

/// <summary>节点列表项 — 仅包含名称和颜色标记。</summary>
public class NodeEntry
{
    public NodeDescriptor Descriptor { get; }
    public string DisplayName => Descriptor.DisplayName;
    public Brush ColorBrush { get; }

    public NodeEntry(NodeDescriptor descriptor)
    {
        Descriptor = descriptor;
        try
        {
            ColorBrush = new BrushConverter().ConvertFromString(descriptor.Color) as Brush ?? Brushes.Gray;
        }
        catch
        {
            ColorBrush = Brushes.Gray;
        }
    }
}
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
/// 节点创建弹窗 — 两级选择：先选分类，再选节点（仅显示名称）。
/// 使用 ShowDialog() 模态显示，避免 Deactivated 崩溃问题。
/// </summary>
public partial class NodeSearchWindow : Window
{
    private readonly Dictionary<string, List<NodeEntry>> _byCategory;
    private readonly Action<string>? _onSelected;
    private string? _selectedCategory;

    public NodeSearchWindow(IEnumerable<NodeDescriptor> descriptors, Action<string>? onSelected)
    {
        InitializeComponent();
        _onSelected = onSelected;

        _byCategory = descriptors
            .GroupBy(d => string.IsNullOrEmpty(d.Category) ? "未分类" : d.Category)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(d => d.DisplayName).Select(d => new NodeEntry(d)).ToList());

        ShowCategories();
        Loaded += (_, _) => SearchBox.Focus();
    }

    // ════════════════ 分类视图 ════════════════

    private void ShowCategories()
    {
        _selectedCategory = null;
        TitleText.Text = "选择分类";
        BackButton.Visibility = Visibility.Collapsed;
        CategoryList.Visibility = Visibility.Visible;
        NodeList.Visibility = Visibility.Collapsed;
        SearchBox.Text = "";
        FilterCurrentView();
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
        CategoryList.Visibility = Visibility.Collapsed;
        NodeList.Visibility = Visibility.Visible;
        SearchBox.Text = "";
        FilterCurrentView();
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
        // 从点击的元素向上查找 ListBoxItem
        DependencyObject? dep = e.OriginalSource as DependencyObject;
        while (dep != null && dep is not ListBoxItem)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is ListBoxItem item && item.DataContext is NodeEntry entry)
        {
            e.Handled = true; // 阻止事件继续传播
            // 延迟到事件处理完毕后再关闭窗口，避免在 Preview 事件中同步关闭导致鼠标状态混乱
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
        // 无选中时回退到第一项（供 Enter 键使用）
        if (entry == null && NodeList.Items.Count > 0)
            entry = NodeList.Items[0] as NodeEntry;

        if (entry != null)
        {
            _onSelected?.Invoke(entry.Descriptor.TypeId);
            Close();
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        ShowCategories();
    }

    // ════════════════ 搜索过滤 ════════════════

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterCurrentView();
    }

    private void FilterCurrentView()
    {
        var text = SearchBox.Text;

        if (_selectedCategory == null)
        {
            // 过滤分类
            var categories = _byCategory.Keys
                .Where(k => k.Contains(text, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k)
                .ToList();
            CategoryList.ItemsSource = categories;
            StatusText.Text = categories.Count > 0
                ? $"{categories.Count} 个分类 · 点击进入 · Esc 取消"
                : "无匹配分类";
        }
        else
        {
            // 过滤节点
            var nodes = _byCategory[_selectedCategory]
                .Where(n => n.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase))
                .ToList();
            NodeList.ItemsSource = nodes;
            StatusText.Text = nodes.Count > 0
                ? $"{nodes.Count} 个节点 · 双击创建 · Backspace 返回 · Esc 取消"
                : "无匹配节点";
        }
    }

    // ════════════════ 键盘交互 ════════════════

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (_selectedCategory != null)
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
                // 搜索框为空时 Backspace 返回分类
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
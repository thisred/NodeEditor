using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditor.Core.Discovery;
using NodeEditor.ViewModels;

namespace NodeEditor.Controls;

/// <summary>
/// 节点创建弹窗 — 分类浏览 + 全局搜索。
/// </summary>
public partial class NodeSearchWindow : ContentView
{
    private readonly Dictionary<string, List<NodeEntry>> _byCategory;
    private readonly List<NodeEntry> _allNodes;
    private readonly Action<string>? _onSelected;
    private string? _selectedCategory;

    public event Action? RequestClose;

    // UI elements
    private readonly SearchBar _searchBox;
    private readonly CollectionView _categoryList;
    private readonly CollectionView _nodeList;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Button _backButton;
    private readonly Grid _rootGrid;

    public NodeSearchWindow(IEnumerable<NodeDescriptor> descriptors, Action<string>? onSelected)
    {
        _onSelected = onSelected;

        var entries = descriptors
            .OrderBy(d => d.DisplayName)
            .Select(d => new NodeEntry(d))
            .ToList();

        _allNodes = entries;
        _byCategory = entries
            .GroupBy(e => string.IsNullOrEmpty(e.Descriptor.Category) ? "未分类" : e.Descriptor.Category)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Build UI
        _backButton = new Button
        {
            Text = "◀",
            BackgroundColor = Colors.Transparent,
            TextColor = Color.FromArgb("#999"),
            FontSize = 14,
            WidthRequest = 30,
            HeightRequest = 30,
            Padding = 0,
            IsVisible = false
        };
        _backButton.Clicked += (_, _) => ShowCategories();

        _titleLabel = new Label
        {
            Text = "选择分类",
            TextColor = Color.FromArgb("#89DCEB"),
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            VerticalTextAlignment = TextAlignment.Center
        };

        var titleRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }
        };
        titleRow.Add(_backButton, 0, 0);
        titleRow.Add(_titleLabel, 1, 0);

        _searchBox = new SearchBar
        {
            BackgroundColor = Color.FromArgb("#1E1E1E"),
            TextColor = Color.FromArgb("#CCC"),
            CancelButtonColor = Color.FromArgb("#89DCEB"),
            Placeholder = "搜索节点...",
            PlaceholderColor = Color.FromArgb("#666"),
            FontSize = 13
        };
        _searchBox.TextChanged += (_, _) => FilterView();

        var topPanel = new VerticalStackLayout { Spacing = 6 };
        topPanel.Add(titleRow);
        topPanel.Add(_searchBox);

        _categoryList = new CollectionView
        {
            BackgroundColor = Colors.Transparent,
            SelectionMode = SelectionMode.Single
        };
        _categoryList.SelectionChanged += CategoryList_SelectionChanged;
        _categoryList.ItemTemplate = new DataTemplate(() =>
        {
            var label = new Label { FontSize = 13, TextColor = Color.FromArgb("#CCC"), Padding = new Thickness(10, 7) };
            label.SetBinding(Label.TextProperty, ".");
            return label;
        });

        _nodeList = new CollectionView
        {
            BackgroundColor = Colors.Transparent,
            SelectionMode = SelectionMode.Single,
            IsVisible = false
        };
        _nodeList.SelectionChanged += NodeList_SelectionChanged;
        _nodeList.ItemTemplate = new DataTemplate(() =>
        {
            var layout = new HorizontalStackLayout { Spacing = 8, Padding = new Thickness(10, 6) };
            var dot = new BoxView { WidthRequest = 8, HeightRequest = 8, CornerRadius = 4 };
            dot.SetBinding(BoxView.ColorProperty, "Color");
            var name = new Label { FontSize = 13, TextColor = Color.FromArgb("#CCC"), VerticalTextAlignment = TextAlignment.Center };
            name.SetBinding(Label.TextProperty, "DisplayName");
            layout.Add(dot);
            layout.Add(name);
            return layout;
        });

        _statusLabel = new Label
        {
            TextColor = Color.FromArgb("#666"),
            FontSize = 11,
            Padding = new Thickness(12, 4)
        };

        _rootGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };
        _rootGrid.Add(topPanel, 0, 0);

        var contentGrid = new Grid();
        contentGrid.Add(_categoryList);
        contentGrid.Add(_nodeList);
        _rootGrid.Add(contentGrid, 0, 1);
        _rootGrid.Add(_statusLabel, 0, 2);

        var border = new Border
        {
            Content = _rootGrid,
            BackgroundColor = Color.FromArgb("#2D2D2D"),
            Stroke = Color.FromArgb("#555"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(8),
            WidthRequest = 300,
            HeightRequest = 400
        };

        // 拦截 tap 事件，防止冒泡到 overlay 层导致窗口关闭
        var absorbTap = new TapGestureRecognizer();
        absorbTap.Tapped += (_, _) => { /* 拦截，不冒泡 */ };
        border.GestureRecognizers.Add(absorbTap);

        Content = border;
        ShowCategories();
    }

    private void ShowCategories()
    {
        _selectedCategory = null;
        _titleLabel.Text = "选择分类";
        _backButton.IsVisible = false;
        _categoryList.IsVisible = true;
        _nodeList.IsVisible = false;
        _searchBox.Text = "";
        FilterView();
    }

    private void CategoryList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_categoryList.SelectedItem is string category)
            EnterCategory(category);
        // 忽略空选择（防止 CollectionView 清除选择时误关窗口）
    }

    private void EnterCategory(string category)
    {
        _selectedCategory = category;
        _titleLabel.Text = category;
        _backButton.IsVisible = true;
        _categoryList.IsVisible = false;
        _nodeList.IsVisible = true;
        _searchBox.Text = "";
        FilterView();
    }

    private void NodeList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_nodeList.SelectedItem is NodeEntry entry)
        {
            _onSelected?.Invoke(entry.Descriptor.TypeId);
            RequestClose?.Invoke();
        }
    }

    private void FilterView()
    {
        var text = _searchBox.Text;

        if (!string.IsNullOrEmpty(text))
        {
            _categoryList.IsVisible = false;
            _nodeList.IsVisible = true;
            var nodes = _allNodes
                .Where(n => n.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase))
                .ToList();
            _nodeList.ItemsSource = nodes;
            _statusLabel.Text = nodes.Count > 0
                ? $"{nodes.Count} 个节点 · 点击创建"
                : "无匹配节点";
            return;
        }

        if (_selectedCategory == null)
        {
            _categoryList.IsVisible = true;
            _nodeList.IsVisible = false;
            var categories = _byCategory.Keys.OrderBy(k => k).ToList();
            _categoryList.ItemsSource = categories;
            _statusLabel.Text = categories.Count > 0
                ? $"{categories.Count} 个分类 · 点击进入"
                : "无分类";
        }
        else
        {
            _categoryList.IsVisible = false;
            _nodeList.IsVisible = true;
            var nodes = _byCategory[_selectedCategory];
            _nodeList.ItemsSource = nodes;
            _statusLabel.Text = nodes.Count > 0
                ? $"{nodes.Count} 个节点 · 点击创建"
                : "无节点";
        }
    }
}

/// <summary>节点列表项</summary>
public class NodeEntry
{
    public NodeDescriptor Descriptor { get; }
    public string DisplayName => Descriptor.DisplayName;
    public Color Color { get; }

    public NodeEntry(NodeDescriptor descriptor)
    {
        Descriptor = descriptor;
        try { Color = Microsoft.Maui.Graphics.Color.FromArgb(descriptor.Color); }
        catch { Color = Colors.Gray; }
    }
}

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
    private readonly Entry _searchBox;
    private readonly VerticalStackLayout _itemContainer;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Button _backButton;

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
            TextColor = Color.FromArgb("#89DCEB"),
            FontSize = 12,
            WidthRequest = 24,
            HeightRequest = 24,
            Padding = 0,
            BorderWidth = 0,
            CornerRadius = 12,
            Opacity = 0,
            InputTransparent = true
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
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 4
        };
        titleRow.Add(_backButton, 0, 0);
        titleRow.Add(_titleLabel, 1, 0);

        // ── 搜索框（Entry 代替 SearchBar，避免原生控件白底问题） ──
        // 颜色由全局 Entry 样式 + WinUI TextControl* 资源统一控制
        _searchBox = new Entry
        {
            Placeholder = "搜索节点...",
            HeightRequest = 30,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing
        };
        _searchBox.TextChanged += (_, _) => FilterView();

        var topPanel = new VerticalStackLayout { Spacing = 8 };
        topPanel.Add(titleRow);
        topPanel.Add(_searchBox);

        // ── 列表区域：ScrollView + BindableLayout（确保滚动手势可靠） ──
        _itemContainer = new VerticalStackLayout { Spacing = 1 };
        var scrollView = new ScrollView
        {
            Content = _itemContainer,
            BackgroundColor = Colors.Transparent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Default
        };

        // ── 状态栏 ──
        _statusLabel = new Label
        {
            TextColor = Color.FromArgb("#7A7A7A"),
            FontSize = 11,
            Padding = new Thickness(4, 6, 4, 0)
        };

        var rootGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 6
        };
        rootGrid.Add(topPanel, 0, 0);
        rootGrid.Add(scrollView, 0, 1);
        rootGrid.Add(_statusLabel, 0, 2);

        var border = new Border
        {
            Content = rootGrid,
            BackgroundColor = Color.FromArgb("#2B2B2B"),
            Stroke = Color.FromArgb("#4A4A52"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(10),
            WidthRequest = 300,
            HeightRequest = 400,
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(Colors.Black),
                Opacity = 0.55f,
                Radius = 24,
                Offset = new Point(0, 8)
            }
        };

        // 拦截 tap 事件，防止冒泡到 overlay 层导致窗口关闭
        var absorbTap = new TapGestureRecognizer();
        absorbTap.Tapped += (_, _) => { /* 拦截，不冒泡 */ };
        border.GestureRecognizers.Add(absorbTap);

        Content = border;
        ShowCategories();

        // 弹窗打开后自动聚焦搜索框
        Loaded += async (_, _) =>
        {
            await Task.Delay(80);
            _searchBox.Focus();
        };
    }

    private void ShowCategories()
    {
        _selectedCategory = null;
        _titleLabel.Text = "选择分类";
        _backButton.Opacity = 0;
        _backButton.InputTransparent = true;
        _searchBox.Text = "";
        FilterView();
    }

    private void EnterCategory(string category)
    {
        _selectedCategory = category;
        _titleLabel.Text = category;
        _backButton.Opacity = 1;
        _backButton.InputTransparent = false;
        _searchBox.Text = "";
        FilterView();
    }

    private void FilterView()
    {
        _itemContainer.Clear();
        var text = _searchBox.Text;

        if (!string.IsNullOrEmpty(text))
        {
            var nodes = _allNodes
                .Where(n => n.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var node in nodes)
                _itemContainer.Add(CreateNodeItem(node));
            _statusLabel.Text = nodes.Count > 0
                ? $"{nodes.Count} 个节点 · 点击创建"
                : "无匹配节点";
            return;
        }

        if (_selectedCategory == null)
        {
            var categories = _byCategory.Keys.OrderBy(k => k).ToList();
            foreach (var category in categories)
                _itemContainer.Add(CreateCategoryItem(category));
            _statusLabel.Text = categories.Count > 0
                ? $"{categories.Count} 个分类 · 点击进入"
                : "无分类";
        }
        else
        {
            var nodes = _byCategory[_selectedCategory];
            foreach (var node in nodes)
                _itemContainer.Add(CreateNodeItem(node));
            _statusLabel.Text = nodes.Count > 0
                ? $"{nodes.Count} 个节点 · 点击创建"
                : "无节点";
        }
    }

    // ── 列表项构建（带悬停高亮） ──

    private View CreateCategoryItem(string category)
    {
        var nameLabel = new Label
        {
            Text = category,
            FontSize = 13,
            TextColor = Color.FromArgb("#E0E0E0"),
            VerticalTextAlignment = TextAlignment.Center
        };

        var grid = new Grid
        {
            Padding = new Thickness(10, 8),
            BackgroundColor = Colors.Transparent
        };
        grid.Add(nameLabel, 0, 0);

        AttachItemInteraction(grid, () => EnterCategory(category));
        return grid;
    }

    private View CreateNodeItem(NodeEntry entry)
    {
        var dot = new Border
        {
            WidthRequest = 8,
            HeightRequest = 8,
            BackgroundColor = entry.Color,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
            VerticalOptions = LayoutOptions.Center
        };
        var nameLabel = new Label
        {
            Text = entry.DisplayName,
            FontSize = 13,
            TextColor = Color.FromArgb("#E0E0E0"),
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };

        var layout = new HorizontalStackLayout
        {
            Spacing = 10,
            Padding = new Thickness(10, 7),
            BackgroundColor = Colors.Transparent
        };
        layout.Add(dot);
        layout.Add(nameLabel);

        var container = new Grid { BackgroundColor = Colors.Transparent };
        container.Add(layout);

        AttachItemInteraction(container, () =>
        {
            _onSelected?.Invoke(entry.Descriptor.TypeId);
            RequestClose?.Invoke();
        });
        return container;
    }

    /// <summary>为列表项附加悬停高亮 + 点击行为。</summary>
    private static void AttachItemInteraction(View item, Action onClick)
    {
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => item.BackgroundColor = Color.FromArgb("#3A3A42");
        pointer.PointerExited += (_, _) => item.BackgroundColor = Colors.Transparent;
        item.GestureRecognizers.Add(pointer);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onClick();
        item.GestureRecognizers.Add(tap);
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

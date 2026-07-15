using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.ComponentModel;
using NodeEditor.Core.Discovery;
using NodeEditor.Wpf.ViewModels;

namespace NodeEditor.Wpf;

/// <summary>
/// 主窗口交互逻辑 — 处理画布交互（节点拖拽、连线创建、缩放平移）和端口位置追踪。
/// </summary>
public partial class MainWindow : Window
{
    private readonly EditorViewModel _viewModel;

    // ── 端口元素追踪（用于计算连线端点坐标） ──
    private readonly Dictionary<PortViewModel, Ellipse> _portElements = new();

    // ── 节点拖拽状态 ──
    private bool _isDraggingNode;
    private NodeViewModel? _draggingNode;
    private Dictionary<NodeViewModel, (double X, double Y)> _dragStartPositions = new();
    private Point _dragStartCanvasPos;

    // ── 框选状态 ──
    private bool _isBoxSelecting;
    private Point _boxSelectStart;

    // ── 连线拖拽状态 ──
    private bool _isDraggingConnection;

    // ── 画布平移状态 ──
    private bool _isPanning;
    private Point _panStartScreenPos;
    private double _panStartX, _panStartY;

    // ── 全屏状态 ──
    private bool _isFullscreen;
    private WindowStyle _prevWindowStyle;
    private WindowState _prevWindowState;
    private ResizeMode _prevResizeMode;
    private double _prevWidth, _prevHeight;
    private double _prevLeft, _prevTop;

    // ── 小地图状态 ──
    private bool _isNavigatingMinimap;
    private bool _minimapUpdatePending;
    private double _minimapMinX, _minimapMinY;
    private double _minimapScale = 1;
    private double _minimapOffsetX, _minimapOffsetY;

    // ── 网格画刷变换（随画布缩放/平移） ──
    private readonly ScaleTransform _gridScale = new(1, 1);
    private readonly TranslateTransform _gridTranslate = new(0, 0);

    // ── 节点搜索弹窗引用（非模态，支持替换） ──
    private NodeSearchWindow? _searchWindow;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new EditorViewModel();
        DataContext = _viewModel;

        // 文件对话框
        _viewModel.RequestSavePath = () =>
        {
            var dlg = new SaveFileDialog
            {
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                DefaultExt = ".json",
                FileName = "graph.json"
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        };

        _viewModel.RequestOpenPath = () =>
        {
            var dlg = new OpenFileDialog
            {
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*"
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        };

        // 端口位置更新（每次布局变化时触发）
        CanvasContainer.LayoutUpdated += OnLayoutUpdated;

        // 中间按钮事件（XAML 不支持直接绑定）
        CanvasArea.MouseDown += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
                CanvasArea_MouseMiddleButtonDown(s, e);
        };
        CanvasArea.MouseUp += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
                CanvasArea_MouseMiddleButtonUp(s, e);
        };

        Loaded += (_, _) => SetupGridBackground();

        // 小地图更新（事件驱动，不依赖 LayoutUpdated）
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Nodes.CollectionChanged += (_, _) => ScheduleMinimapUpdate();
        _viewModel.NodePositionsChanged += () => ScheduleMinimapUpdate();
        CanvasArea.SizeChanged += (_, _) => ScheduleMinimapUpdate();

        // 导入后延迟刷新端口坐标（等待 UI 渲染完成）
        _viewModel.GraphImported += () =>
        {
            for (int i = 0; i < 3; i++)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    CanvasContainer.UpdateLayout();
                    OnLayoutUpdated(null, EventArgs.Empty);
                }), System.Windows.Threading.DispatcherPriority.Render);
            }
        };

        // F11 全屏切换
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    // ════════════════ F11 全屏切换 ════════════════

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _viewModel.CopySelectedNodes();
            var count = _viewModel.GetSelectedNodes().Count;
            if (count > 0) UpdateStatus($"已复制 {count} 个节点");
            e.Handled = true;
        }
        else if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // 粘贴到视口中心偏移 30px
            _viewModel.PasteNodes(30, 30);
            UpdateStatus("已粘贴节点");
            e.Handled = true;
        }
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            // 进入全屏
            _prevWindowStyle = WindowStyle;
            _prevWindowState = WindowState;
            _prevResizeMode = ResizeMode;
            _prevWidth = Width;
            _prevHeight = Height;
            _prevLeft = Left;
            _prevTop = Top;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            _isFullscreen = true;
            UpdateStatus("全屏模式 · F11 退出");
        }
        else
        {
            // 退出全屏
            WindowStyle = _prevWindowStyle;
            ResizeMode = _prevResizeMode;
            WindowState = _prevWindowState;
            Width = _prevWidth;
            Height = _prevHeight;
            Left = _prevLeft;
            Top = _prevTop;
            _isFullscreen = false;
            UpdateStatus("");
        }
    }

    // ════════════════ 小地图更新触发 ════════════════

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.Zoom) or
            nameof(EditorViewModel.PanX) or
            nameof(EditorViewModel.PanY))
        {
            // 更新网格画刷变换（跟随缩放/平移）
            _gridScale.ScaleX = _viewModel.Zoom;
            _gridScale.ScaleY = _viewModel.Zoom;
            _gridTranslate.X = _viewModel.PanX;
            _gridTranslate.Y = _viewModel.PanY;
            // 更新小地图
            ScheduleMinimapUpdate();
        }
    }

    // ════════════════ 坐标转换 ════════════════

    /// <summary>屏幕坐标（相对 CanvasArea）→ 画布逻辑坐标</summary>
    private Point ScreenToCanvas(Point screenPoint)
    {
        return new Point(
            (screenPoint.X - _viewModel.PanX) / _viewModel.Zoom,
            (screenPoint.Y - _viewModel.PanY) / _viewModel.Zoom
        );
    }

    // ════════════════ 端口位置追踪 ════════════════

    private void Port_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Ellipse ellipse && ellipse.Tag is PortViewModel portVm)
        {
            _portElements[portVm] = ellipse;
        }
    }

    private void Port_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Ellipse ellipse && ellipse.Tag is PortViewModel portVm)
        {
            _portElements.Remove(portVm);
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        foreach (var (portVm, ellipse) in _portElements.ToList())
        {
            if (ellipse == null) continue;
            try
            {
                if (!ellipse.IsVisible) continue;
                var center = new Point(ellipse.ActualWidth / 2, ellipse.ActualHeight / 2);
                var transform = ellipse.TransformToVisual(CanvasContainer);
                var pos = transform.Transform(center);
                portVm.CenterX = pos.X;
                portVm.CenterY = pos.Y;
                portVm.IsPositionValid = true;
            }
            catch
            {
                // 元素可能尚未完全渲染，跳过
            }
        }
    }

    // ════════════════ 节点选中 ════════════════

    private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is NodeViewModel nodeVm)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                // Ctrl+点击：切换选中
                _viewModel.ToggleNodeSelection(nodeVm);
            }
            else if (!nodeVm.IsSelected)
            {
                // 单击未选中的节点：只选这个
                _viewModel.SelectNode(nodeVm);
            }

            e.Handled = true; // 阻止冒泡到 CanvasArea
        }
    }

    private void CanvasArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 左键点击画布时关闭搜索弹窗
        CloseSearchWindow();

        // 点击空白区域：启动框选
        if (!_isDraggingConnection)
        {
            // Ctrl 保留已有选中，非 Ctrl 清除
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                _viewModel.DeselectAll();

            _isBoxSelecting = true;
            _boxSelectStart = e.GetPosition(CanvasArea);
            SelectionRect.Visibility = Visibility.Visible;
            CanvasArea.CaptureMouse();
        }
    }

    // ════════════════ 节点拖拽 ════════════════

    private void NodeHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is NodeViewModel nodeVm)
        {
            // Ctrl+点击：切换选中，不启动拖拽
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _viewModel.ToggleNodeSelection(nodeVm);
                e.Handled = true;
                return;
            }

            // 如果点击的节点未选中，先只选中它
            if (!nodeVm.IsSelected)
                _viewModel.SelectNode(nodeVm);

            // 收集所有选中节点，准备多节点拖拽
            var selected = _viewModel.GetSelectedNodes();
            if (selected.Count == 0) selected.Add(nodeVm);

            _isDraggingNode = true;
            _draggingNode = nodeVm;
            _dragStartCanvasPos = ScreenToCanvas(e.GetPosition(CanvasArea));
            _dragStartPositions = selected.ToDictionary(n => n, n => (n.X, n.Y));
            CanvasArea.CaptureMouse();
            e.Handled = true;
        }
    }

    // ════════════════ 连线创建 ════════════════

    private void OutputPort_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Ellipse ellipse && ellipse.Tag is PortViewModel portVm)
        {
            _isDraggingConnection = true;
            _viewModel.StartPendingConnection(portVm);
            var pos = ScreenToCanvas(e.GetPosition(CanvasArea));
            _viewModel.UpdatePendingConnection(pos.X, pos.Y);
            CanvasArea.CaptureMouse();
            e.Handled = true;
        }
    }

    private void InputPort_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 使用 CaptureMouse 后此事件不会在拖拽时触发，
        // 但保留以处理非拖拽场景的点击
        e.Handled = true;
    }

    /// <summary>通过 HitTest 查找鼠标下的端口</summary>
    private PortViewModel? FindPortUnderMouse(Point canvasContainerPos)
    {
        var result = VisualTreeHelper.HitTest(CanvasContainer, canvasContainerPos);
        if (result?.VisualHit == null) return null;

        var current = result.VisualHit;
        while (current != null)
        {
            if (current is Ellipse ell && ell.Tag is PortViewModel pvm)
                return pvm;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    // ════════════════ 鼠标移动（统一处理拖拽、连线、平移） ════════════════

    private void CanvasArea_MouseMove(object sender, MouseEventArgs e)
    {
        var screenPos = e.GetPosition(CanvasArea);

        if (_isDraggingNode && _draggingNode != null)
        {
            var canvasPos = ScreenToCanvas(screenPos);
            var deltaX = canvasPos.X - _dragStartCanvasPos.X;
            var deltaY = canvasPos.Y - _dragStartCanvasPos.Y;

            // 移动所有选中节点
            foreach (var (node, (startX, startY)) in _dragStartPositions)
            {
                node.X = startX + deltaX;
                node.Y = startY + deltaY;
            }
        }
        else if (_isDraggingConnection)
        {
            var canvasPos = ScreenToCanvas(screenPos);
            _viewModel.UpdatePendingConnection(canvasPos.X, canvasPos.Y);
        }
        else if (_isPanning)
        {
            _viewModel.PanX = _panStartX + (screenPos.X - _panStartScreenPos.X);
            _viewModel.PanY = _panStartY + (screenPos.Y - _panStartScreenPos.Y);
        }
        else if (_isBoxSelecting)
        {
            // 更新选框位置和大小
            var x = Math.Min(_boxSelectStart.X, screenPos.X);
            var y = Math.Min(_boxSelectStart.Y, screenPos.Y);
            var w = Math.Abs(screenPos.X - _boxSelectStart.X);
            var h = Math.Abs(screenPos.Y - _boxSelectStart.Y);

            SelectionRect.Width = w;
            SelectionRect.Height = h;
            Canvas.SetLeft(SelectionRect, x);
            Canvas.SetTop(SelectionRect, y);
        }
    }

    // ════════════════ 鼠标释放 ════════════════

    private void CanvasArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingConnection)
        {
            // 通过 HitTest 查找鼠标下的端口
            var containerPos = e.GetPosition(CanvasContainer);
            var targetPort = FindPortUnderMouse(containerPos);
            _viewModel.CompletePendingConnection(targetPort);
            _isDraggingConnection = false;
            CanvasArea.ReleaseMouseCapture();
            UpdateStatus(targetPort != null ? "连线已创建" : "连线已取消");
        }

        if (_isDraggingNode)
        {
            _isDraggingNode = false;
            _draggingNode = null;
            _dragStartPositions.Clear();
            CanvasArea.ReleaseMouseCapture();
        }

        if (_isBoxSelecting)
        {
            _isBoxSelecting = false;
            SelectionRect.Visibility = Visibility.Collapsed;
            CanvasArea.ReleaseMouseCapture();

            // 将屏幕坐标选框转为画布坐标，选中范围内节点
            var endPos = e.GetPosition(CanvasArea);
            var canvasStart = ScreenToCanvas(_boxSelectStart);
            var canvasEnd = ScreenToCanvas(endPos);

            // 只有拖拽了一定距离才算框选（避免误触）
            if (Math.Abs(endPos.X - _boxSelectStart.X) > 3 ||
                Math.Abs(endPos.Y - _boxSelectStart.Y) > 3)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                    _viewModel.AddNodesInRectToSelection(canvasStart.X, canvasStart.Y, canvasEnd.X, canvasEnd.Y);
                else
                    _viewModel.SelectNodesInRect(canvasStart.X, canvasStart.Y, canvasEnd.X, canvasEnd.Y);
            }
        }
    }

    // ════════════════ 缩放 ════════════════

    private void CanvasArea_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mousePos = e.GetPosition(CanvasArea);
        var oldZoom = _viewModel.Zoom;
        var newZoom = oldZoom * (e.Delta > 0 ? 1.1 : 1.0 / 1.1);
        newZoom = Math.Clamp(newZoom, 0.2, 3.0);

        // 以鼠标位置为缩放中心
        var ratio = newZoom / oldZoom;
        _viewModel.PanX = mousePos.X - (mousePos.X - _viewModel.PanX) * ratio;
        _viewModel.PanY = mousePos.Y - (mousePos.Y - _viewModel.PanY) * ratio;
        _viewModel.Zoom = newZoom;
        e.Handled = true;
    }

    // ════════════════ 平移 ════════════════

    private void CanvasArea_MouseMiddleButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPanning = true;
        _panStartScreenPos = e.GetPosition(CanvasArea);
        _panStartX = _viewModel.PanX;
        _panStartY = _viewModel.PanY;
        CanvasArea.CaptureMouse();
        e.Handled = true;
    }

    private void CanvasArea_MouseMiddleButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            CanvasArea.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    // ════════════════ 右键创建节点 ════════════════

    private void CanvasArea_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 关闭已有的弹窗（第二次右键替换第一次）
        CloseSearchWindow();

        // 获取鼠标在画布逻辑坐标系中的位置
        var screenPos = e.GetPosition(CanvasArea);
        var canvasPos = ScreenToCanvas(screenPos);

        // 弹出搜索窗口（非模态）
        _searchWindow = new NodeSearchWindow(
            _viewModel.Discovery.GetAllDescriptors(),
            typeId =>
            {
                _viewModel.CreateNode(typeId, canvasPos.X, canvasPos.Y);
                var desc = _viewModel.Discovery.GetAllDescriptors()
                    .FirstOrDefault(d => d.TypeId == typeId);
                UpdateStatus(desc != null ? $"已创建节点: {desc.DisplayName}" : "节点已创建");
            });

        // 在鼠标位置显示窗口
        var mouseScreenPos = PointToScreen(screenPos);
        _searchWindow.Left = mouseScreenPos.X;
        _searchWindow.Top = mouseScreenPos.Y;
        _searchWindow.Owner = this;

        // 窗口关闭时清空引用
        _searchWindow.Closed += (_, _) => _searchWindow = null;

        _searchWindow.Show();
        _searchWindow.Activate(); // 确保非模态窗口立即获得焦点

        e.Handled = true;
    }

    /// <summary>关闭搜索弹窗（如果存在）</summary>
    private void CloseSearchWindow()
    {
        if (_searchWindow != null)
        {
            var w = _searchWindow;
            _searchWindow = null;
            w.Close();
        }
    }

    // ════════════════ 连线删除 ════════════════

    private void Connection_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Path path && path.DataContext is ConnectionViewModel connVm)
        {
            _viewModel.DeleteConnection(connVm);
            UpdateStatus("连线已删除");
            e.Handled = true;
        }
    }

    // ════════════════ 网格背景（无限平铺） ════════════════

    private void SetupGridBackground()
    {
        const double gridSize = 20;
        const double majorSize = 100;

        var bgColor = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        var minorColor = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        var majorColor = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));

        // 细线：每 20px 一条
        var minorGeometry = new GeometryGroup();
        for (double x = 0; x <= majorSize; x += gridSize)
        {
            minorGeometry.Children.Add(new LineGeometry(new Point(x, 0), new Point(x, majorSize)));
            minorGeometry.Children.Add(new LineGeometry(new Point(0, x), new Point(majorSize, x)));
        }

        // 粗线：每 100px 一条（在 tile 边缘）
        var majorGeometry = new GeometryGroup();
        majorGeometry.Children.Add(new LineGeometry(new Point(0, 0), new Point(majorSize, 0)));
        majorGeometry.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, majorSize)));

        var drawing = new DrawingGroup();
        // 深色背景填充
        drawing.Children.Add(new GeometryDrawing(bgColor, null, new RectangleGeometry(new Rect(0, 0, majorSize, majorSize))));
        drawing.Children.Add(new GeometryDrawing(null, new Pen(minorColor, 0.5), minorGeometry));
        drawing.Children.Add(new GeometryDrawing(null, new Pen(majorColor, 0.8), majorGeometry));

        // 初始化画刷变换（跟随画布缩放/平移）
        _gridScale.ScaleX = _viewModel.Zoom;
        _gridScale.ScaleY = _viewModel.Zoom;
        _gridTranslate.X = _viewModel.PanX;
        _gridTranslate.Y = _viewModel.PanY;

        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(_gridScale);
        transformGroup.Children.Add(_gridTranslate);

        // 设置在 CanvasArea（非变换元素）上，通过 brush.Transform 跟随画布
        CanvasArea.Background = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, majorSize, majorSize),
            ViewportUnits = BrushMappingMode.Absolute,
            Transform = transformGroup
        };
    }

    /// <summary>延迟更新小地图（避免 LayoutUpdated 循环）</summary>
    private void ScheduleMinimapUpdate()
    {
        if (_minimapUpdatePending) return;
        _minimapUpdatePending = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _minimapUpdatePending = false;
            UpdateMinimap();
        }));
    }

    // ════════════════ 小地图 ════════════════

    private void UpdateMinimap()
    {
        MinimapCanvas.Children.Clear();

        if (_viewModel.Nodes.Count == 0 || CanvasArea.ActualWidth < 1)
        {
            MinimapBorder.Visibility = Visibility.Collapsed;
            return;
        }

        MinimapBorder.Visibility = Visibility.Visible;

        const double minimapW = 180;
        const double minimapH = 120;
        const double nodeW = 180;
        const double nodeH = 80;
        const double padding = 50;

        // 计算所有节点的包围盒
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var node in _viewModel.Nodes)
        {
            minX = Math.Min(minX, node.X);
            minY = Math.Min(minY, node.Y);
            maxX = Math.Max(maxX, node.X + nodeW);
            maxY = Math.Max(maxY, node.Y + nodeH);
        }

        // 包含当前视口范围
        double viewMinX = -_viewModel.PanX / _viewModel.Zoom;
        double viewMinY = -_viewModel.PanY / _viewModel.Zoom;
        double viewMaxX = viewMinX + CanvasArea.ActualWidth / _viewModel.Zoom;
        double viewMaxY = viewMinY + CanvasArea.ActualHeight / _viewModel.Zoom;

        minX = Math.Min(minX, viewMinX) - padding;
        minY = Math.Min(minY, viewMinY) - padding;
        maxX = Math.Max(maxX, viewMaxX) + padding;
        maxY = Math.Max(maxY, viewMaxY) + padding;

        double contentW = maxX - minX;
        double contentH = maxY - minY;
        if (contentW < 1 || contentH < 1) return;

        _minimapScale = Math.Min(minimapW / contentW, minimapH / contentH);
        _minimapMinX = minX;
        _minimapMinY = minY;
        _minimapOffsetX = (minimapW - contentW * _minimapScale) / 2;
        _minimapOffsetY = (minimapH - contentH * _minimapScale) / 2;

        // 绘制节点
        foreach (var node in _viewModel.Nodes)
        {
            var rect = new Rectangle
            {
                Width = Math.Max(2, nodeW * _minimapScale),
                Height = Math.Max(2, nodeH * _minimapScale),
                Fill = GetBrushFromColor(node.Color),
                Opacity = 0.8,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(rect, _minimapOffsetX + (node.X - _minimapMinX) * _minimapScale);
            Canvas.SetTop(rect, _minimapOffsetY + (node.Y - _minimapMinY) * _minimapScale);
            MinimapCanvas.Children.Add(rect);
        }

        // 绘制视口矩形
        var vpRect = new Rectangle
        {
            Width = Math.Max(1, (viewMaxX - viewMinX) * _minimapScale),
            Height = Math.Max(1, (viewMaxY - viewMinY) * _minimapScale),
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            Opacity = 0.7,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(vpRect, _minimapOffsetX + (viewMinX - _minimapMinX) * _minimapScale);
        Canvas.SetTop(vpRect, _minimapOffsetY + (viewMinY - _minimapMinY) * _minimapScale);
        MinimapCanvas.Children.Add(vpRect);
    }

    private static Brush GetBrushFromColor(string color)
    {
        try
        {
            return new BrushConverter().ConvertFromString(color) as Brush ?? Brushes.Gray;
        }
        catch
        {
            return Brushes.Gray;
        }
    }

    private void Minimap_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isNavigatingMinimap = true;
        MinimapCanvas.CaptureMouse();
        NavigateToMinimapPos(e.GetPosition(MinimapCanvas));
        e.Handled = true;
    }

    private void Minimap_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isNavigatingMinimap)
        {
            NavigateToMinimapPos(e.GetPosition(MinimapCanvas));
            e.Handled = true;
        }
    }

    private void Minimap_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isNavigatingMinimap = false;
        MinimapCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void NavigateToMinimapPos(Point minimapPos)
    {
        if (_minimapScale == 0) return;

        double canvasX = (minimapPos.X - _minimapOffsetX) / _minimapScale + _minimapMinX;
        double canvasY = (minimapPos.Y - _minimapOffsetY) / _minimapScale + _minimapMinY;

        _viewModel.PanX = CanvasArea.ActualWidth / 2 - canvasX * _viewModel.Zoom;
        _viewModel.PanY = CanvasArea.ActualHeight / 2 - canvasY * _viewModel.Zoom;
    }

    // ════════════════ 辅助 ════════════════

    private void UpdateStatus(string text)
    {
        StatusText.Text = text;
    }
}
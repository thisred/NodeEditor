using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using NodeEditor.Controls;
using NodeEditor.Core.Models;
using NodeEditor.ViewModels;

namespace NodeEditor;

public partial class MainPage : ContentPage
{
     private const double NodeWidth = NodeViewModel.ApproxWidth;

    private readonly EditorViewModel _viewModel;
    private readonly ConnectionDrawable _connectionDrawable;
    private readonly GridBackgroundDrawable _gridDrawable;

    // ── 节点拖拽状态 ──
    private bool _isDraggingNode;
    private bool _isDraggingNodeInternal; // 拖拽中抑制 PositionChanged 级联刷新
    private NodeViewModel? _draggingNode;
    private Dictionary<NodeViewModel, (double X, double Y)> _dragStartPositions = new();

    // ── 框选状态 ──
    private bool _isBoxSelecting;
    private Point _boxSelectStart;

    // ── 连线拖拽状态 ──
    private bool _isDraggingConnection;
    private PortViewModel? _connectionSourcePort;
    // 连线拖拽期间用定时器轮询全局光标位置：即使指针移出画布/窗口，连线端点仍跟随光标。
    // （连线起点在节点卡片上，用 CapturePointer 会与卡片的 MAUI 手势冲突导致"全局变色"，
    //  故改用 GetCursorPos 轮询，不依赖指针捕获。）
    private IDispatcherTimer? _connectionDragTimer;

    // ── 端口元素追踪（缓存布局元数据：方向/所属节点/行索引，避免每帧 IndexOf） ──
    private readonly Dictionary<PortViewModel, (bool IsInput, NodeViewModel Node, int Index)> _portElements = new();

    // ── 节点视图映射 ──
    private readonly Dictionary<NodeViewModel, View> _nodeElements = new();

    // ── 搜索弹窗 ──
    private NodeSearchWindow? _searchWindow;
    // 搜索弹窗在画布坐标系中的屏幕矩形（用于判断点击是否落在弹窗外 → 关闭）
    private Rect _searchWindowScreenRect;

    // ── 自动保存 ──
    private IDispatcherTimer? _autoSaveTimer;

    // ── 日志面板状态 ──
    private bool _isLogCollapsed;

    // ── 小地图 ──
    private readonly MinimapDrawable _minimapDrawable;

    // ── 键盘状态 ──
    private bool _isCtrlPressed;
    private Microsoft.UI.Xaml.UIElement? _rootElement;
    private Microsoft.UI.Xaml.UIElement? _nativeCanvasElement;
    private bool _isFullScreen;

    // ── 中键平移 ──
    private bool _isMiddleButtonPanning;
    private Point _middleButtonLastPos;

    // ── 标题栏 ──
    private Label? _titleBarStatusText;

    // ── 日志面板（原生 TextBox 引用，用于去边框、允许选中、自动滚底） ──
    private Microsoft.UI.Xaml.Controls.TextBox? _logTextBox;

    // ── 菜单栏状态 ──
    private Microsoft.UI.Xaml.Controls.MenuFlyout? _currentMenuFlyout;
    private Button? _currentMenuButton;
    private IDispatcherTimer? _menuHoverTimer;
    private int _menuGeneration; // 防止旧菜单的异步 Closed 事件停掉新定时器

    public MainPage()
    {
        InitializeComponent();

        _viewModel = new EditorViewModel();
        BindingContext = _viewModel;

        // 初始化连线绘制（GraphicsView 渲染 + HitTest 命中检测）
        _connectionDrawable = new ConnectionDrawable();
        ConnectionsView.Drawable = _connectionDrawable;

        // 初始化网格背景
        _gridDrawable = new GridBackgroundDrawable();
        GridBackgroundView.Drawable = _gridDrawable;

        // 初始化小地图
        _minimapDrawable = new MinimapDrawable();
        MinimapView.Drawable = _minimapDrawable;

        // 小地图点击跳转：把点击处的画布点移动到视口中心
        var minimapTap = new TapGestureRecognizer();
        minimapTap.Tapped += OnMinimapTapped;
        MinimapView.GestureRecognizers.Add(minimapTap);

        // 文件对话框回调
        _viewModel.RequestSavePath = RequestSavePathAsync;
        _viewModel.RequestOpenPath = RequestOpenPathAsync;
        _viewModel.FileExplorer.RequestFolderSelection = RequestFolderSelectionAsync;
        _viewModel.FileExplorer.RequestNewFileName = RequestNewFileName;
        _viewModel.FileExplorer.RequestOverwriteConfirmation = RequestOverwriteConfirmation;

        // 订阅节点集合变化
        _viewModel.Nodes.CollectionChanged += OnNodesCollectionChanged;
        _viewModel.Connections.CollectionChanged += OnConnectionsCollectionChanged;

        // 画布手势
        SetupCanvasGestures();

        // 分割条拖拽在 OnAppearing 里用原生指针事件挂载（Handler 就绪后）

        // 裁剪画布区域：节点平移/缩放后不溢出到工具栏/侧栏上方
        // （ClipsToBounds 非 BindableProperty，XAML 不可设；改用 Clip + SizeChanged）
        CanvasArea.SizeChanged += (s, e) =>
        {
            CanvasArea.Clip = new Microsoft.Maui.Controls.Shapes.RectangleGeometry(
                new Rect(0, 0, CanvasArea.Width, CanvasArea.Height));
            // 窗口大小改变后，CenterX/CenterY 变化，必须重绘连线和网格
            ApplyCanvasTransform();
        };

        // 订阅日志
        ExecutionLogger.LogAdded += OnLogAdded;
        ExecutionLogger.Cleared += OnLogCleared;

        // 日志控件原生定制：移除原生 TextBox 边框/背景。
        // WinUI 3 只读 TextBox 本身就支持选中/复制（无需 WPF 的 IsReadOnlyCaretEnabled）。
        LogOutput.HandlerChanged += (s, e) =>
        {
            if (LogOutput.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox logTb)
            {
                _logTextBox = logTb;
                logTb.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
                logTb.BorderBrush = null;
                logTb.Padding = new Microsoft.UI.Xaml.Thickness(6, 4, 6, 4);
            }
        };

        // 窗口标题
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.WindowTitle))
                Title = _viewModel.WindowTitle;
        };
        Title = _viewModel.WindowTitle;

        // 订阅缩放/平移变化
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.NodePositionsChanged += OnNodePositionsChanged;

        // 图导入后刷新
        _viewModel.GraphImported += () => Dispatcher.Dispatch(RefreshConnections);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // 自动保存定时器：复用单例，避免 OnAppearing 多次触发时累积多个定时器
        if (_autoSaveTimer == null)
        {
            _autoSaveTimer = Dispatcher.CreateTimer();
            _autoSaveTimer.Interval = TimeSpan.FromSeconds(5);
            _autoSaveTimer.Tick += (_, _) =>
            {
                try { _viewModel.SaveCurrent(); } catch { }
            };
        }
        _autoSaveTimer.Start();

        // 挂载 WinUI 原生右键事件和键盘快捷键（延迟到 Handler 就绪）
        Dispatcher.Dispatch(() =>
        {
            // 右键事件
            if (CanvasArea.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement nativeCanvas)
            {
                _nativeCanvasElement = nativeCanvas;
                nativeCanvas.RightTapped += OnNativeCanvasRightTapped;
                // 使用 AddHandler + handledEventsToo: true，确保即使子元素（节点）
                // 已处理事件，中键平移仍能捕获到 PointerPressed/Moved/Released
                nativeCanvas.AddHandler(
                    Microsoft.UI.Xaml.UIElement.PointerPressedEvent,
                    new Microsoft.UI.Xaml.Input.PointerEventHandler(OnNativeCanvasPointerPressed),
                    true);
                nativeCanvas.AddHandler(
                    Microsoft.UI.Xaml.UIElement.PointerMovedEvent,
                    new Microsoft.UI.Xaml.Input.PointerEventHandler(OnNativeCanvasPointerMoved),
                    true);
                nativeCanvas.AddHandler(
                    Microsoft.UI.Xaml.UIElement.PointerReleasedEvent,
                    new Microsoft.UI.Xaml.Input.PointerEventHandler(OnNativeCanvasPointerReleased),
                    true);
                // 滚轮缩放
                nativeCanvas.PointerWheelChanged += OnNativeCanvasPointerWheelChanged;
            }

            // 背景层禁用命中测试
            // （Windows 上 GraphicsView 即使 InputTransparent=True 仍可能拦截输入，这里在原生层禁用）
            if (GridBackgroundView.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement bgEl)
                bgEl.IsHitTestVisible = false;
            if (ConnectionsView.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement connEl)
                connEl.IsHitTestVisible = false;

            // 键盘快捷键（挂到 WinUI Window Content，确保全局捕获）
            var winuiWindow = this.Window?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            if (winuiWindow?.Content is Microsoft.UI.Xaml.UIElement windowContent)
            {
                _rootElement = windowContent;
                windowContent.PreviewKeyDown += OnNativeKeyDown;
                windowContent.PreviewKeyUp += OnNativeKeyUp;
            }

            // 设置 .NET MAUI TitleBar（菜单栏占据标题栏位置）
            SetupTitleBar();
        });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _autoSaveTimer?.Stop();
        _menuHoverTimer?.Stop();
        _connectionDragTimer?.Stop();

        // 取消挂载原生事件
        if (_nativeCanvasElement != null)
        {
            _nativeCanvasElement.RightTapped -= OnNativeCanvasRightTapped;
            _nativeCanvasElement.RemoveHandler(
                Microsoft.UI.Xaml.UIElement.PointerPressedEvent,
                new Microsoft.UI.Xaml.Input.PointerEventHandler(OnNativeCanvasPointerPressed));
            _nativeCanvasElement.RemoveHandler(
                Microsoft.UI.Xaml.UIElement.PointerMovedEvent,
                new Microsoft.UI.Xaml.Input.PointerEventHandler(OnNativeCanvasPointerMoved));
            _nativeCanvasElement.RemoveHandler(
                Microsoft.UI.Xaml.UIElement.PointerReleasedEvent,
                new Microsoft.UI.Xaml.Input.PointerEventHandler(OnNativeCanvasPointerReleased));
            _nativeCanvasElement.PointerWheelChanged -= OnNativeCanvasPointerWheelChanged;
            _nativeCanvasElement = null;
        }

        // 取消挂载键盘快捷键
        if (_rootElement != null)
        {
            _rootElement.PreviewKeyDown -= OnNativeKeyDown;
            _rootElement.PreviewKeyUp -= OnNativeKeyUp;
            _rootElement = null;
        }
    }

    private void OnNativeCanvasRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        var nativeElement = sender as Microsoft.UI.Xaml.UIElement;
        var position = e.GetPosition(nativeElement);
        ShowNodeSearchWindow(new Point(position.X, position.Y));
    }

    private void OnNativeCanvasPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // 搜索弹窗打开时：点击弹窗内部交给弹窗自身处理（选择分类/节点）；
        // 点击弹窗外部则关闭弹窗（Windows 标准的"点其他区域即消失"行为）。
        // 画布用 AddHandler(handledEventsToo:true) 挂事件，弹窗内元素上的点击也会冒泡到这里，
        // 因此这里用弹窗矩形来区分内外，而不能一律 return。
        if (SearchOverlay.IsVisible)
        {
            var overlayPoint = e.GetCurrentPoint(sender as Microsoft.UI.Xaml.UIElement);
            // 右键：交给 RightTapped 处理（它会先关旧弹窗、再在新位置打开）。
            // 这里绝不能 e.Handled，否则会抑制 WinUI 的 RightTapped 手势 → 第二次右键无反应。
            if (overlayPoint.Properties.IsRightButtonPressed) return;

            var overlayPos = overlayPoint.Position;
            if (!_searchWindowScreenRect.Contains(new Point(overlayPos.X, overlayPos.Y)))
            {
                CloseSearchWindow();
                e.Handled = true;
            }
            return;
        }

        var point = e.GetCurrentPoint(sender as Microsoft.UI.Xaml.UIElement);
        var props = point.Properties;
        var pos = point.Position;
    
        if (props.IsMiddleButtonPressed)
        {
            // 中键平移：捕获指针，使指针移出画布范围后仍能收到 Moved/Released，
            // 拖拽不会"停在背景边缘"。仅拖拽期间捕获、抬起即释放，不影响按钮视觉状态。
            e.Handled = true;
            _isMiddleButtonPanning = true;
            _middleButtonLastPos = new Point(pos.X, pos.Y);
            (sender as Microsoft.UI.Xaml.UIElement)?.CapturePointer(e.Pointer);
        }
        else if (props.IsLeftButtonPressed)
        {
            var canvasPos = ScreenToCanvas(new Point(pos.X, pos.Y));
    
            // 优先检测：是否按在端口上 → 开始拖拽连线
            var port = FindPortNearPosition(canvasPos);
            if (port != null)
            {
                e.Handled = true;
                _connectionSourcePort = port;
                _isDraggingConnection = true;
                _potentialBoxSelect = false;
                _viewModel.StartPendingConnection(port);
                UpdateStatus("拖拽到目标端口以创建连线");
                // 不用 CapturePointer（起点在节点卡片上，会与卡片 MAUI 手势冲突导致"全局变色"）。
                // 改用定时器轮询全局光标，使指针移出画布/窗口时连线端点仍跟随。
                StartConnectionDragTracking();
                return;
            }
    
            // 左键：点中节点则交由 MAUI 手势（Tap/Pan）处理，不框选；
            // 点空白则准备框选。
            if (IsPointOnNode(canvasPos))
            {
                _potentialBoxSelect = false;
            }
            else
            {
                e.Handled = true;
                _potentialBoxSelect = true;
                _boxSelectStart = new Point(pos.X, pos.Y);
                // 此时尚不确定是框选还是普通点击，暂不捕获指针；
                // 待 Moved 中确认为拖拽（越过阈值）后再 CapturePointer。
            }
        }
    }

    private void OnNativeCanvasPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(sender as Microsoft.UI.Xaml.UIElement).Position;

        // 中键平移
        if (_isMiddleButtonPanning)
        {
            e.Handled = true;
            var dx = pos.X - _middleButtonLastPos.X;
            var dy = pos.Y - _middleButtonLastPos.Y;
            _middleButtonLastPos = new Point(pos.X, pos.Y);
            _viewModel.PanX += dx;
            _viewModel.PanY += dy;
            // PanX/PanY 变化通过 OnViewModelPropertyChanged → ApplyCanvasTransform() 自动生效
            return;
        }

        // 连线拖拽中：只更新临时连线终点（端口位置未变，无需全量刷新）
        if (_isDraggingConnection)
        {
            e.Handled = true;
            var canvasPos = ScreenToCanvas(new Point(pos.X, pos.Y));
            _viewModel.UpdatePendingConnection(canvasPos.X, canvasPos.Y);
            SyncConnectionDrawable();
            ConnectionsView.Invalidate();
            return;
        }

        // 节点拖拽中，不框选
        if (_isDraggingNode)
        {
            _potentialBoxSelect = false;
            return;
        }

        if (!_potentialBoxSelect) return;

        var dxs = pos.X - _boxSelectStart.X;
        var dys = pos.Y - _boxSelectStart.Y;

        if (!_isBoxSelecting && (Math.Abs(dxs) > 3 || Math.Abs(dys) > 3))
        {
            _isBoxSelecting = true;
            SelectionRect.IsVisible = true;
        }

        if (_isBoxSelecting)
        {
            var x = Math.Min(_boxSelectStart.X, pos.X);
            var y = Math.Min(_boxSelectStart.Y, pos.Y);
            AbsoluteLayout.SetLayoutBounds(SelectionRect, new Rect(x, y, Math.Abs(dxs), Math.Abs(dys)));
        }
    }

    private void OnNativeCanvasPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // 搜索框打开时，画布不处理指针（同 Pressed），避免点分类项时误关窗口。
        if (SearchOverlay.IsVisible) return;

        var point = e.GetCurrentPoint(sender as Microsoft.UI.Xaml.UIElement);
        var props = point.Properties;
        var pos = point.Position;

        if (_isMiddleButtonPanning && !props.IsMiddleButtonPressed)
        {
            _isMiddleButtonPanning = false;
            e.Handled = true;
            return;
        }

        // 连线拖拽完成：查找目标端口
        if (_isDraggingConnection)
        {
            e.Handled = true;

            var canvasPos = ScreenToCanvas(new Point(pos.X, pos.Y));
            var targetPort = FindPortNearPosition(canvasPos);

            // 排除源端口自身
            if (targetPort == _connectionSourcePort) targetPort = null;

            _viewModel.CompletePendingConnection(targetPort);
            _connectionSourcePort = null;
            _isDraggingConnection = false;
            StopConnectionDragTracking();
            RefreshConnections();
            UpdateStatus(targetPort != null ? "连线已创建" : "连线已取消");
            return;
        }

        // 节点拖拽由 MAUI 手势处理
        if (_isDraggingNode)
        {
            _potentialBoxSelect = false;
            return;
        }

        if (_isBoxSelecting)
        {
            e.Handled = true;

            _isBoxSelecting = false;
            _potentialBoxSelect = false;
            SelectionRect.IsVisible = false;

            var dx = pos.X - _boxSelectStart.X;
            var dy = pos.Y - _boxSelectStart.Y;
            if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3)
            {
                var canvasStart = ScreenToCanvas(_boxSelectStart);
                var canvasEnd = ScreenToCanvas(new Point(pos.X, pos.Y));
                _viewModel.SelectNodesInRect(canvasStart.X, canvasStart.Y, canvasEnd.X, canvasEnd.Y);
                UpdateNodeSelectionVisuals();
            }
        }
        else if (_potentialBoxSelect)
        {
            e.Handled = true;

            // 点击空白
            _potentialBoxSelect = false;
            CloseSearchWindow();

            var canvasPos = ScreenToCanvas(new Point(pos.X, pos.Y));
            if (IsPointOnNode(canvasPos)) return;

            var hitConn = _connectionDrawable.HitTest(canvasPos.X, canvasPos.Y, 15);
            if (hitConn != null)
                UpdateStatus($"连线: {hitConn.SourcePort.DisplayName} → {hitConn.TargetPort.DisplayName}  (双击删除)");
            else
            {
                _viewModel.DeselectAll();
                UpdateNodeSelectionVisuals();
            }
        }
    }

    // ── 连线拖拽：全局光标轮询（覆盖"指针移出画布/窗口"场景，不依赖 CapturePointer）──

    private void StartConnectionDragTracking()
    {
        if (_connectionDragTimer == null)
        {
            _connectionDragTimer = Dispatcher.CreateTimer();
            _connectionDragTimer.Interval = TimeSpan.FromMilliseconds(16);
            _connectionDragTimer.Tick += OnConnectionDragTick;
        }
        _connectionDragTimer.Start();
    }

    private void StopConnectionDragTracking() => _connectionDragTimer?.Stop();

    private void OnConnectionDragTick(object? sender, EventArgs e)
    {
        if (!_isDraggingConnection)
        {
            StopConnectionDragTracking();
            return;
        }

        var canvasPos = GetCursorCanvasPosition();
        if (canvasPos == null) return;

        // 指针在窗口外松开时，原生 Released 事件不会触发，这里用全局按键状态兜底检测松开。
        bool leftDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        if (!leftDown)
        {
            var targetPort = FindPortNearPosition(canvasPos.Value);
            if (targetPort == _connectionSourcePort) targetPort = null;
            _viewModel.CompletePendingConnection(targetPort);
            _connectionSourcePort = null;
            _isDraggingConnection = false;
            StopConnectionDragTracking();
            RefreshConnections();
            UpdateStatus(targetPort != null ? "连线已创建" : "连线已取消");
            return;
        }

        _viewModel.UpdatePendingConnection(canvasPos.Value.X, canvasPos.Value.Y);
        SyncConnectionDrawable();
        ConnectionsView.Invalidate();
    }

    /// <summary>用全局光标位置（可在窗口外）换算成画布坐标。</summary>
    private Point? GetCursorCanvasPosition()
    {
        if (_nativeCanvasElement == null) return null;
        if (!GetCursorPos(out var pt)) return null;
        var hwnd = GetActiveWindowHandle();
        if (hwnd == IntPtr.Zero) return null;
        ScreenToClient(hwnd, ref pt);

        // GetCursorPos/ScreenToClient 返回物理像素，TransformToVisual 用逻辑像素(DIPs)，需统一。
        var scale = _nativeCanvasElement.XamlRoot?.RasterizationScale ?? 1.0;
        var logicalX = pt.X / scale;
        var logicalY = pt.Y / scale;

        // 画布元素在窗口根坐标系中的原点（逻辑像素）
        var canvasOrigin = _nativeCanvasElement.TransformToVisual(null)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        return ScreenToCanvas(new Point(logicalX - canvasOrigin.X, logicalY - canvasOrigin.Y));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    private const int VK_LBUTTON = 0x01;

    private void OnNativeKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var key = e.Key;
        if (key == Windows.System.VirtualKey.Control || key == Windows.System.VirtualKey.LeftControl || key == Windows.System.VirtualKey.RightControl)
        {
            _isCtrlPressed = true;
            return;
        }

        // 焦点在文本输入控件（日志框/属性输入框）时，Delete/Ctrl+C/Ctrl+V 应作用于文本
        // （选中复制/粘贴/删除字符），不触发全局节点快捷键；Ctrl+S、F11 仍保持全局。
        var inTextBox = e.OriginalSource is Microsoft.UI.Xaml.Controls.TextBox;

        if (key == Windows.System.VirtualKey.Delete && !inTextBox)
        {
            var count = _viewModel.GetSelectedNodes().Count;
            _viewModel.DeleteSelectedNode();
            UpdateNodeSelectionVisuals();
            UpdateStatus(count > 0 ? $"已删除 {count} 个节点" : "");
            e.Handled = true;
        }
        else if (_isCtrlPressed && key == Windows.System.VirtualKey.C && !inTextBox)
        {
            _viewModel.CopySelectedNodes();
            var count = _viewModel.GetSelectedNodes().Count;
            UpdateStatus(count > 0 ? $"已复制 {count} 个节点" : "未选中任何节点");
            e.Handled = true;
        }
        else if (_isCtrlPressed && key == Windows.System.VirtualKey.V && !inTextBox)
        {
            _viewModel.PasteNodes(30, 30);
            UpdateNodeSelectionVisuals();
            UpdateStatus("已粘贴节点");
            e.Handled = true;
        }
        else if (_isCtrlPressed && key == Windows.System.VirtualKey.S)
        {
            _ = Save_Click_Async();
            e.Handled = true;
        }
        else if (key == Windows.System.VirtualKey.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
    }

    private void OnNativeKeyUp(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var key = e.Key;
        if (key == Windows.System.VirtualKey.Control || key == Windows.System.VirtualKey.LeftControl || key == Windows.System.VirtualKey.RightControl)
        {
            _isCtrlPressed = false;
        }
    }

    // 菜单项点击包装：调用全屏切换
    private void ToggleFullScreen_Click(object? sender, EventArgs e) => ToggleFullScreen();

    private async System.Threading.Tasks.Task Save_Click_Async()
    {
        try
        {
            if (_viewModel.SaveCurrent())
                UpdateStatus($"已保存 {_viewModel.CurrentFileName}");
            else
            {
                var path = await RequestSavePathAsync();
                if (!string.IsNullOrEmpty(path))
                {
                    _viewModel.Serializer.SerializeToFile(_viewModel.Graph, path);
                    UpdateStatus($"已保存到 {System.IO.Path.GetFileName(path)}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Save] 异常: {ex.Message}");
        }
    }

    private void ToggleFullScreen()
    {
        try
        {
            var window = this.Window?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            if (window == null) return;

            var appWindow = window.AppWindow;
            if (_isFullScreen)
            {
                appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
                _isFullScreen = false;
                UpdateStatus("已退出全屏");
            }
            else
            {
                appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
                _isFullScreen = true;
                UpdateStatus("全屏模式 · F11 退出");
            }
        }
        catch { }
    }

    /// <summary>
    /// 设置 MAUI 原生 TitleBar（纯跨平台，无平台特定代码）。
    /// </summary>
    private void SetupTitleBar()
    {
        try
        {
            if (this.Window == null) return;

            var titleBar = (TitleBar)Resources["AppTitleBar"];

            // 先清空，避免 OnAppearing 多次触发时 PassthroughElements 累积重复项
            titleBar.PassthroughElements.Clear();

            // LeadingContent: 菜单按钮 → PassthroughElements（可点击）+ 悬停切换
            if (titleBar.LeadingContent is HorizontalStackLayout leadingLayout)
            {
                foreach (var child in leadingLayout.Children)
                {
                    if (child is Button menuBtn)
                    {
                        titleBar.PassthroughElements.Add(menuBtn);
                    }
                }
            }

            // Content: 执行按钮
            if (titleBar.Content is Button execBtn)
            {
                execBtn.Command = _viewModel.ExecuteCommand;
                titleBar.PassthroughElements.Add(execBtn);
            }

            // 状态文字（在页面左下角）
            _titleBarStatusText = TitleBarStatusLabel;

            this.Window.TitleBar = titleBar;

            // 延迟一帧，等原生 TitleBar 渲染完毕后调整布局
            Dispatcher.Dispatch(() =>
            {
                try
                {
                    var platformView = titleBar.Handler?.PlatformView as Microsoft.UI.Xaml.FrameworkElement;
                    if (platformView == null) return;
                    var contentPresenter = FindChild<Microsoft.UI.Xaml.Controls.ContentPresenter>(
                        platformView, "Content");
                    if (contentPresenter != null)
                    {
                        // 让 ContentPresenter 填满整行并居中内部按钮
                        contentPresenter.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
                        contentPresenter.HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center;

                        // 背景设为 null：ContentPresenter 不参与命中测试，
                        // 指针事件穿透到 TitleBar 根元素 → 触发窗口拖拽，
                        // 同时左侧菜单按钮仍可点击。
                        contentPresenter.Background = null;
                    }

                    // 记录原生元素，用位移把按钮精确挪到窗口正中（模板列布局不可靠，改用测量校正）
                    _titleBarPlatformView = platformView;
                    if (titleBar.Content is Button eBtn)
                        _execButtonNative = eBtn.Handler?.PlatformView as Microsoft.UI.Xaml.FrameworkElement;

                    CenterExecButton();
                    // 窗口尺寸变化时重新校正居中
                    platformView.SizeChanged -= OnTitleBarSizeChanged;
                    platformView.SizeChanged += OnTitleBarSizeChanged;
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TitleBar] 异常: {ex.Message}");
        }
    }

    // ── 标题栏执行按钮居中 ──
    private Microsoft.UI.Xaml.FrameworkElement? _titleBarPlatformView;
    private Microsoft.UI.Xaml.FrameworkElement? _execButtonNative;

    private void OnTitleBarSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
        => CenterExecButton();

    /// <summary>
    /// 把标题栏执行按钮精确移到窗口水平正中。
    /// MAUI TitleBar 的 Content 区在菜单栏之后的 star 列里居中 → 视觉偏右；
    /// 这里测量按钮当前中心与标题栏真实中心的差，用 RenderTransform 位移校正。
    /// </summary>
    private void CenterExecButton()
    {
        var root = _titleBarPlatformView;
        var btn = _execButtonNative;
        if (root == null || btn == null) return;

        try
        {
            // 先清除旧位移，测量按钮"自然"位置
            btn.RenderTransform = null;
            root.UpdateLayout();

            var width = root.ActualWidth;
            if (width <= 0 || btn.ActualWidth <= 0) return;

            var bounds = btn.TransformToVisual(root)
                .TransformBounds(new Windows.Foundation.Rect(0, 0, btn.ActualWidth, btn.ActualHeight));
            var currentCenter = bounds.X + bounds.Width / 2.0;
            var delta = width / 2.0 - currentCenter;

            btn.RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform { X = delta };
        }
        catch { }
    }

    /// <summary>
    /// 菜单打开时，定时轮询光标位置，检测是否悬停在其他菜单按钮上（Windows 标准行为）。
    /// MenuFlyout 的 Popup 会捕获指针，标题栏收不到 PointerMoved，所以用定时器代替。
    /// </summary>
    private void OnMenuHoverTimerTick(object? sender, EventArgs e)
    {
        if (_currentMenuFlyout == null || _currentMenuButton == null)
        {
            _menuHoverTimer?.Stop();
            return;
        }

        try
        {
            // 获取当前光标的屏幕坐标（物理像素）
            GetCursorPos(out var pt);

            // 转换为窗口客户区坐标（仍是物理像素）
            var hwnd = GetActiveWindowHandle();
            if (hwnd == IntPtr.Zero) return;
            ScreenToClient(hwnd, ref pt);

            if (this.Window?.TitleBar is not TitleBar titleBar) return;
            if (titleBar.LeadingContent is not HorizontalStackLayout layout) return;

            foreach (var child in layout.Children)
            {
                if (child is Button btn && btn != _currentMenuButton
                    && btn.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement nativeBtn)
                {
                    // 获取 DPI 缩放因子：GetCursorPos/ScreenToClient 返回物理像素，
                    // TransformToVisual 返回逻辑像素(DIPs)，必须统一坐标系。
                    var scale = nativeBtn.XamlRoot?.RasterizationScale ?? 1.0;
                    var logicalX = pt.X / scale;
                    var logicalY = pt.Y / scale;

                    // 将按钮边界变换到窗口根坐标系（逻辑像素）
                    var btnBounds = nativeBtn.TransformToVisual(null)
                        .TransformBounds(new Windows.Foundation.Rect(
                            0, 0, nativeBtn.ActualWidth, nativeBtn.ActualHeight));
                    if (btnBounds.Contains(new Windows.Foundation.Point(logicalX, logicalY)))
                    {
                        ShowMenuForButton(btn);
                        return;
                    }
                }
            }
        }
        catch { }
    }

    // ── P/Invoke：获取光标位置（用于菜单 hover 切换） ──
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    /// <summary>在 WinUI 可视化树中递归查找指定名称的子元素</summary>
    private static T? FindChild<T>(Microsoft.UI.Xaml.DependencyObject parent, string name) where T : Microsoft.UI.Xaml.FrameworkElement
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T fe && fe.Name == name)
                return fe;
            var result = FindChild<T>(child, name);
            if (result != null)
                return result;
        }
        return null;
    }

    // ════════════════ 画布手势设置 ════════════════
    
    private void SetupCanvasGestures()
    {
        // 双击手势（删除连线）
        var doubleTapGesture = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        doubleTapGesture.Tapped += OnCanvasDoubleTapped;
        CanvasArea.GestureRecognizers.Add(doubleTapGesture);
    
        // 左键框选、中键平移、滚轮缩放均使用 WinUI 原生事件
        // （见 OnNativeCanvasPointer* / OnNativeCanvasPointerWheelChanged）
        // 不再使用 MAUI 的 Pointer/Pan/Pinch 手势——它们会与子元素（节点头部、端口）
        // 的同名手势竞争并"抢走"事件，导致节点不能拖拽、端口不能连线。
    }

    // ════════════════ 点击/指针事件 ════════════════

    private void OnCanvasDoubleTapped(object? sender, TappedEventArgs e)
    {
        var pos = e.GetPosition(CanvasArea) ?? default;
        var canvasPos = ScreenToCanvas(pos);

        // 双击在节点上时不处理连线（节点双击用于跳转源码，见 OpenNodeSourceInIde）
        if (IsPointOnNode(canvasPos)) return;

        // 双击删除连线
        var hitConn = _connectionDrawable.HitTest(canvasPos.X, canvasPos.Y, 15);
        if (hitConn != null)
        {
            _viewModel.DeleteConnection(hitConn);
            RefreshConnections();
            UpdateStatus("连线已删除");
        }
    }

    /// <summary>
    /// 在 IDE（Rider）中打开节点类的源码定义。
    /// 通过 SourceLocator 定位类定义的文件与行号，再用 jetbrains:// 协议跳转。
    /// </summary>
    private void OpenNodeSourceInIde(NodeViewModel nodeVm)
    {
        try
        {
            var className = nodeVm.Node.GetType().Name;
            var (path, line) = Helpers.SourceLocator.FindClassDefinition(className);
            if (path == null)
            {
                UpdateStatus($"未找到 {className} 的源码定义");
                return;
            }

            if (TryOpenInRider(path, line))
            {
                UpdateStatus($"已在 IDE 打开 {System.IO.Path.GetFileName(path)}:{line}");
            }
            else
            {
                // 兜底：jetbrains:// 协议（依赖 Toolbox Daemon 转发，可能不可靠）
                var url = $"jetbrains://rider/open?file={Uri.EscapeDataString(path)}&line={line}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                UpdateStatus($"已尝试通过协议打开 {System.IO.Path.GetFileName(path)}:{line}");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"打开源码失败: {ex.Message}");
        }
    }

    /// <summary>直接调用 rider64.exe 打开文件并定位到指定行（Rider 单实例，会转发给运行中的实例）。</summary>
    private static bool TryOpenInRider(string path, int line)
    {
        // 1. 优先从运行中的 Rider 进程取 exe 路径（避免硬编码版本号）
        string? riderExe = null;
        try
        {
            riderExe = System.Diagnostics.Process.GetProcessesByName("rider64")
                .FirstOrDefault(p => !p.HasExited)?.MainModule?.FileName;
        }
        catch { /* 访问进程模块可能需要权限，失败则走扫描 */ }

        // 2. 兜底：扫描默认安装目录（取最新版本）
        if (string.IsNullOrEmpty(riderExe) || !System.IO.File.Exists(riderExe))
        {
            riderExe = System.IO.Directory.EnumerateDirectories(@"C:\Program Files\JetBrains", "JetBrains Rider*")
                .Select(d => System.IO.Path.Combine(d, "bin", "rider64.exe"))
                .Where(System.IO.File.Exists)
                .OrderByDescending(s => s)
                .FirstOrDefault();
        }

        if (string.IsNullOrEmpty(riderExe)) return false;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = riderExe,
            Arguments = $"--line {line} \"{path}\"",
            UseShellExecute = false
        });
        return true;
    }

    // 框选启动标志：左键按下时若未点中节点则置 true，节点/端口 Pan Started 时置 false
    private bool _potentialBoxSelect;

    /// <summary>判断画布坐标是否落在某个节点上（用于区分点击空白与节点）</summary>
    private bool IsPointOnNode(Point canvasPos)
    {
        const double nodeH = 200; // 近似上限，覆盖端口区域
        foreach (var nodeVm in _viewModel.Nodes)
        {
            if (canvasPos.X >= nodeVm.X && canvasPos.X <= nodeVm.X + NodeWidth &&
                canvasPos.Y >= nodeVm.Y && canvasPos.Y <= nodeVm.Y + nodeH)
                return true;
        }
        return false;
    }

    // ════════════════ 滚轮缩放 ════════════════

    private void OnNativeCanvasPointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Microsoft.UI.Xaml.UIElement);
        var delta = point.Properties.MouseWheelDelta;
        var pos = point.Position;

        var oldZoom = _viewModel.Zoom;
        var newZoom = oldZoom * (delta > 0 ? 1.1 : 1.0 / 1.1);
        newZoom = Math.Clamp(newZoom, 0.2, 3.0);

        // 以鼠标位置为缩放中心（补偿 MAUI 中心锚点）
        var cx = CanvasArea.Width / 2.0;
        var cy = CanvasArea.Height / 2.0;
        var ratio = newZoom / oldZoom;
        _viewModel.PanX = pos.X - (pos.X - _viewModel.PanX) * ratio - cx * (1 - ratio);
        _viewModel.PanY = pos.Y - (pos.Y - _viewModel.PanY) * ratio - cy * (1 - ratio);
        _viewModel.Zoom = newZoom;

        ApplyCanvasTransform();
        e.Handled = true;
    }

    // ════════════════ 画布变换 ════════════════

    private void ApplyCanvasTransform()
    {
        CanvasContainer.TranslationX = _viewModel.PanX;
        CanvasContainer.TranslationY = _viewModel.PanY;
        CanvasContainer.Scale = _viewModel.Zoom;
        // 连线层在 CanvasContainer 外部，需同步变换参数并重绘
        SyncConnectionDrawable();
        ConnectionsView.Invalidate();
        UpdateGridBackground();
        ScheduleMinimapUpdate();
    }

    private Point ScreenToCanvas(Point screenPoint)
    {
        // MAUI Scale 以元素中心为锚点（AnchorX/Y=0.5），需补偿中心偏移
        var cx = CanvasArea.Width / 2.0;
        var cy = CanvasArea.Height / 2.0;
        return new Point(
            (screenPoint.X - _viewModel.PanX - cx * (1 - _viewModel.Zoom)) / _viewModel.Zoom,
            (screenPoint.Y - _viewModel.PanY - cy * (1 - _viewModel.Zoom)) / _viewModel.Zoom
        );
    }

    // ════════════════ 网格背景 ════════════════

    private void UpdateGridBackground()
    {
        _gridDrawable.Zoom = (float)_viewModel.Zoom;
        _gridDrawable.PanX = (float)_viewModel.PanX;
        _gridDrawable.PanY = (float)_viewModel.PanY;
        _gridDrawable.CenterX = (float)(CanvasArea.Width / 2.0);
        _gridDrawable.CenterY = (float)(CanvasArea.Height / 2.0);
        GridBackgroundView.Invalidate();
    }

    // ════════════════ 节点渲染 ════════════════

    private void OnNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            foreach (NodeViewModel node in e.NewItems!)
                AddNodeView(node);
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove)
        {
            foreach (NodeViewModel node in e.OldItems!)
                RemoveNodeView(node);
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var kvp in _nodeElements.ToList())
            {
                CanvasContainer.Children.Remove(kvp.Value);
                _nodeElements.Remove(kvp.Key);
            }
            _portElements.Clear();
        }

        RefreshConnections();
        ScheduleMinimapUpdate();
    }

    private void AddNodeView(NodeViewModel nodeVm)
    {
        var nodeView = CreateNodeCard(nodeVm);
        AbsoluteLayout.SetLayoutBounds(nodeView, new Rect(nodeVm.X, nodeVm.Y, NodeWidth, AbsoluteLayout.AutoSize));
        CanvasContainer.Children.Add(nodeView);
        _nodeElements[nodeVm] = nodeView;
    }

    private void RemoveNodeView(NodeViewModel nodeVm)
    {
        if (_nodeElements.TryGetValue(nodeVm, out var view))
        {
            CanvasContainer.Children.Remove(view);
            _nodeElements.Remove(nodeVm);
        }

        foreach (var port in nodeVm.InputPorts.Concat(nodeVm.OutputPorts))
        {
            _portElements.Remove(port);
        }
    }

    private void UpdateNodePosition(NodeViewModel nodeVm)
    {
        if (_nodeElements.TryGetValue(nodeVm, out var view))
        {
            AbsoluteLayout.SetLayoutBounds(view, new Rect(nodeVm.X, nodeVm.Y, NodeWidth, AbsoluteLayout.AutoSize));
        }
    }

    private View CreateNodeCard(NodeViewModel nodeVm)
    {
        var headerBorder = new Border
        {
            BackgroundColor = ParseColor(nodeVm.Color),
            Padding = new Thickness(8, 4),
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = new CornerRadius(5, 5, 0, 0)
            }
        };
        var headerLabel = new Label
        {
            Text = nodeVm.DisplayName,
            TextColor = Colors.White,
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        headerBorder.Content = headerLabel;

        // 端口区域：根据是否有输入/输出端口决定布局
        var hasInputs = nodeVm.InputPorts.Count > 0;
        var hasOutputs = nodeVm.OutputPorts.Count > 0;

        View portsArea;
        if (hasInputs && hasOutputs)
        {
            // 两侧都有端口：左右分列
            var portsGrid = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
                Padding = new Thickness(0, 2, 0, 3)
            };

            var inputStack = new VerticalStackLayout { Spacing = 0 };
            for (int i = 0; i < nodeVm.InputPorts.Count; i++)
                inputStack.Add(CreatePortView(nodeVm.InputPorts[i], true, nodeVm, i));

            var outputStack = new VerticalStackLayout { Spacing = 0 };
            for (int i = 0; i < nodeVm.OutputPorts.Count; i++)
                outputStack.Add(CreatePortView(nodeVm.OutputPorts[i], false, nodeVm, i));

            portsGrid.Add(inputStack, 0, 0);
            portsGrid.Add(outputStack, 1, 0);
            portsArea = portsGrid;
        }
        else
        {
            // 只有一侧端口：单列全宽
            var stack = new VerticalStackLayout { Spacing = 0, Padding = new Thickness(0, 2, 0, 3) };
            var ports = hasInputs ? nodeVm.InputPorts : nodeVm.OutputPorts;
            for (int i = 0; i < ports.Count; i++)
                stack.Add(CreatePortView(ports[i], hasInputs, nodeVm, i));
            portsArea = stack;
        }

        var contentStack = new VerticalStackLayout { Spacing = 0 };
        contentStack.Add(headerBorder);
        contentStack.Add(portsArea);

        var cardBorder = new Border
        {
            Content = contentStack,
            BackgroundColor = Color.FromArgb("#2D2D30"),
            Stroke = nodeVm.IsSelected ? Color.FromArgb("#007ACC") : Color.FromArgb("#3F3F46"),
            // 厚度保持恒定：选中只切换颜色，不改厚度。否则 Border 内容区会随描边变粗而内缩，
            // 导致选中时节点内文字/端口相对圆点位移（"选中后布局变了"）。
            StrokeThickness = 1.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 5 },
            WidthRequest = NodeWidth
        };

        // 选中手势（支持 Ctrl+Click 多选）
        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += (s, e) =>
        {
            if (_isCtrlPressed)
                _viewModel.ToggleNodeSelection(nodeVm);
            else
                _viewModel.SelectNode(nodeVm);
            UpdateNodeSelectionVisuals();
        };
        cardBorder.GestureRecognizers.Add(tapGesture);

        // 拖拽手势：挂到整块卡片，使节点任意位置（含下半部分端口区）都可拖动
        var dragGesture = new PanGestureRecognizer();
        dragGesture.PanUpdated += (s, e) => OnNodePanUpdated(e, nodeVm);
        cardBorder.GestureRecognizers.Add(dragGesture);

        // 双击手势：在 IDE 中打开节点类的源码定义
        var openSourceGesture = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        openSourceGesture.Tapped += (s, e) => OpenNodeSourceInIde(nodeVm);
        cardBorder.GestureRecognizers.Add(openSourceGesture);

        // 选中状态监听
        nodeVm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(NodeViewModel.IsSelected))
            {
                cardBorder.Stroke = nodeVm.IsSelected ? Color.FromArgb("#007ACC") : Color.FromArgb("#3F3F46");
                // 厚度恒定，仅切颜色（见 cardBorder 创建处说明），避免选中导致内容重排位移
            }
        };

        // ── 节点卡片容器：Grid 包含 Border + 端口圆点 ──
        // 圆点是 Grid 的子元素（不是 Border 的子元素），不会被 Border 裁剪，
        // 且跟随节点的 z-order，不会盖住其他节点。
        var nodeGrid = new Grid { WidthRequest = NodeWidth };
        nodeGrid.Add(cardBorder);

        // 端口圆点（半露在节点边缘外）
        const double headerH = 26;
        const double portRowH = 20;
        const double gridPadTop = 2;
        const double dotSize = 10;
        const double execDotSize = 11;

        void AddDot(PortViewModel portVm, bool isInput, int portIndex)
        {
            var actualDotSize = portVm.IsExec ? execDotSize : dotSize;
            // Y: 行中心对齐（用实际圆点尺寸居中）
            var dotY = headerH + gridPadTop + portIndex * portRowH + (portRowH - actualDotSize) / 2.0;
            // X: 半内半外（圆心在节点边缘上）
            var dotX = isInput ? -actualDotSize / 2.0 : NodeWidth - actualDotSize / 2.0;

            View dot;
            if (portVm.IsExec)
            {
                dot = new Border
                {
                    WidthRequest = execDotSize, HeightRequest = execDotSize,
                    BackgroundColor = portVm.TypeColor,
                    Stroke = Colors.Black.WithAlpha(0.5f), StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = 2 },
                    InputTransparent = true
                };
            }
            else
            {
                dot = new Border
                {
                    WidthRequest = dotSize, HeightRequest = dotSize,
                    BackgroundColor = Color.FromArgb("#2D2D30"),
                    Stroke = portVm.TypeColor, StrokeThickness = 2,
                    StrokeShape = new RoundRectangle { CornerRadius = dotSize / 2.0 },
                    InputTransparent = true
                };
            }

            nodeGrid.Add(dot);
            dot.TranslationX = dotX;
            dot.TranslationY = dotY;
            dot.HorizontalOptions = LayoutOptions.Start;
            dot.VerticalOptions = LayoutOptions.Start;
        }

        // 仅遍历当前节点自身的端口（避免遍历全局 _portElements 造成的 O(N²)）
        for (int i = 0; i < nodeVm.InputPorts.Count; i++)
            AddDot(nodeVm.InputPorts[i], true, i);
        for (int i = 0; i < nodeVm.OutputPorts.Count; i++)
            AddDot(nodeVm.OutputPorts[i], false, i);

        return nodeGrid;
    }

    private View CreatePortView(PortViewModel portVm, bool isInput, NodeViewModel nodeVm, int portIndex)
    {
        // 标签（在节点内部）
        var nameLabel = new Label
        {
            Text = portVm.DisplayName,
            TextColor = Color.FromArgb("#CCCCCC"),
            FontSize = 11,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = isInput ? TextAlignment.Start : TextAlignment.End,
            LineBreakMode = LineBreakMode.NoWrap,
            MaxLines = 1
        };

        // 端口行：只放标签，圆点作为节点卡片 Grid 的子元素单独渲染（见 CreateNodeCard.AddDot）
        var grid = new Grid
        {
            HeightRequest = 20,
            ColumnSpacing = 0,
            Padding = new Thickness(8, 0, 8, 0),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star) }
        };
        grid.Add(nameLabel, 0, 0);

        // 记录端口布局元数据（供连线定位与命中检测使用）
        _portElements[portVm] = (isInput, nodeVm, portIndex);

        return grid;
    }

    private void OnNodePanUpdated(PanUpdatedEventArgs e, NodeViewModel nodeVm)
    {
        // 中键平移画布时，忽略节点的拖拽手势（防止节点跟着动）
        if (_isMiddleButtonPanning) return;
        // 框选进行中，忽略节点拖拽（防止鼠标划过节点时误触发节点拖动）
        if (_potentialBoxSelect || _isBoxSelecting) return;
        // 正在从端口拉连线时，忽略节点拖拽（拖拽手势已扩展到整块卡片，需避免连线时误拖节点）
        if (_isDraggingConnection || _connectionSourcePort != null) return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                if (!nodeVm.IsSelected)
                    _viewModel.SelectNode(nodeVm);
                UpdateNodeSelectionVisuals();

                var selected = _viewModel.GetSelectedNodes();
                if (selected.Count == 0) selected.Add(nodeVm);

                _isDraggingNode = true;
                _isDraggingNodeInternal = true; // 抑制级联刷新
                _potentialBoxSelect = false;
                _draggingNode = nodeVm;
                _dragStartPositions = selected.ToDictionary(n => n, n => (n.X, n.Y));
                break;

            case GestureStatus.Running:
                if (_isDraggingNode)
                {
                    var deltaX = e.TotalX / _viewModel.Zoom;
                    var deltaY = e.TotalY / _viewModel.Zoom;

                    foreach (var (node, (startX, startY)) in _dragStartPositions)
                    {
                        node.X = startX + deltaX;
                        node.Y = startY + deltaY;
                        UpdateNodePosition(node);
                    }
                    RefreshConnections(); // 统一刷新一次（不经过 PositionChanged 级联）
                }
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _isDraggingNode = false;
                _isDraggingNodeInternal = false;
                _draggingNode = null;
                _dragStartPositions.Clear();
                ScheduleMinimapUpdate();
                break;
        }
    }

    // ════════════════ 连线刷新 ════════════════

    private void RefreshConnections()
    {
        UpdateAllPortPositions();
        SyncConnectionDrawable();
        ConnectionsView.Invalidate();
    }

    /// <summary>同步连线绘制数据（变换参数 + 临时连线）。连线列表仅在集合变化时重建。</summary>
    private void SyncConnectionDrawable()
    {
        _connectionDrawable.Zoom = (float)_viewModel.Zoom;
        _connectionDrawable.PanX = (float)_viewModel.PanX;
        _connectionDrawable.PanY = (float)_viewModel.PanY;
        _connectionDrawable.CenterX = (float)(CanvasArea.Width / 2.0);
        _connectionDrawable.CenterY = (float)(CanvasArea.Height / 2.0);

        // 临时连线
        if (_viewModel.HasPendingConnection && _viewModel.PendingSourcePort != null)
        {
            _connectionDrawable.HasPending = true;
            _connectionDrawable.PendingX1 = (float)_viewModel.PendingSourcePort.CenterX;
            _connectionDrawable.PendingY1 = (float)_viewModel.PendingSourcePort.CenterY;
            _connectionDrawable.PendingX2 = (float)_viewModel.PendingX;
            _connectionDrawable.PendingY2 = (float)_viewModel.PendingY;
        }
        else
        {
            _connectionDrawable.HasPending = false;
        }
    }

    /// <summary>重建连线绘制列表（仅在连线集合发生增删时调用，避免每帧分配）。</summary>
    private void RebuildConnectionList()
    {
        _connectionDrawable.Connections = _viewModel.Connections.ToList();
    }

    private void OnConnectionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildConnectionList();
        RefreshConnections();
    }

    private void UpdateAllPortPositions()
    {
        // 与 CreateNodeCard / CreatePortView 中的实际尺寸对应：
        // headerBorder: Padding(8,4) + Label FontSize=11 Bold ≈ 26px 总高
        // portsArea: Padding(0,2,0,3) → 顶部 2px
        // 每个端口行: Grid HeightRequest=20, Spacing=0 → 步进 20px
        const double headerHeight = 26;
        const double portRowHeight = 20;
        const double portSpacing = 0;
        const double gridTopPadding = 2;

        foreach (var (portVm, (isInput, nodeVm, portIndex)) in _portElements)
        {
            var cx = nodeVm.X + (isInput ? 0 : NodeWidth);
            var cy = nodeVm.Y + headerHeight + gridTopPadding
                     + portIndex * (portRowHeight + portSpacing)
                     + portRowHeight / 2.0;
            portVm.SetPositionSilent(cx, cy);
        }
    }

    private PortViewModel? FindPortNearPosition(Point canvasPos)
    {
        const double hitRadius = 30;
        PortViewModel? best = null;
        var bestDist = hitRadius;

        foreach (var (portVm, _) in _portElements)
        {
            if (!portVm.IsPositionValid) continue;
            var dx = portVm.CenterX - canvasPos.X;
            var dy = portVm.CenterY - canvasPos.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = portVm;
            }
        }
        return best;
    }

    // ════════════════ 选中视觉更新 ════════════════

    private void UpdateNodeSelectionVisuals()
    {
        foreach (var (nodeVm, view) in _nodeElements)
        {
            // 节点卡片是 Grid（包含 Border + 端口圆点），找到内部的 Border 更新选中样式
            Border? border = view as Border ?? (view as Grid)?.Children.OfType<Border>().FirstOrDefault();
            if (border != null)
            {
                border.Stroke = nodeVm.IsSelected ? Color.FromArgb("#007ACC") : Color.FromArgb("#3F3F46");
                // 厚度恒定，仅切颜色，避免选中导致内容重排位移
            }
        }
    }

    // ════════════════ 节点搜索弹窗 ════════════════

    private void ShowNodeSearchWindow(Point screenPos)
    {
        CloseSearchWindow();

        var canvasPos = ScreenToCanvas(screenPos);

        _searchWindow = new NodeSearchWindow(
            _viewModel.Discovery.GetAllDescriptors(),
            typeId =>
            {
                _viewModel.CreateNode(typeId, canvasPos.X, canvasPos.Y);
                var desc = _viewModel.Discovery.GetAllDescriptors()
                    .FirstOrDefault(d => d.TypeId == typeId);
                UpdateStatus(desc != null ? $"已创建节点: {desc.DisplayName}" : "节点已创建");
            });

        _searchWindow.RequestClose += CloseSearchWindow;

        // 将弹窗限制在画布可视区域内，避免超出边界被裁剪导致底部项目不可见
        const double winW = 300, winH = 400;
        var posX = Math.Clamp(screenPos.X, 0, Math.Max(0, CanvasArea.Width - winW));
        var posY = Math.Clamp(screenPos.Y, 0, Math.Max(0, CanvasArea.Height - winH));

        _searchWindowScreenRect = new Rect(posX, posY, winW, winH);
        AbsoluteLayout.SetLayoutBounds(_searchWindow, _searchWindowScreenRect);
        SearchContainer.Children.Add(_searchWindow);
        SearchOverlay.IsVisible = true;
    }

    private void CloseSearchWindow()
    {
        if (_searchWindow != null)
        {
            SearchContainer.Children.Remove(_searchWindow);
            _searchWindow = null;
        }
        SearchOverlay.IsVisible = false;
    }

    private void SearchOverlay_Tapped(object? sender, TappedEventArgs e)
    {
        CloseSearchWindow();
    }

    // ════════════════ 小地图 ════════════════

    private bool _minimapUpdatePending;

    private void ScheduleMinimapUpdate()
    {
        if (_minimapUpdatePending) return;
        _minimapUpdatePending = true;
        Dispatcher.Dispatch(() =>
        {
            _minimapUpdatePending = false;
            UpdateMinimap();
        });
    }

    private void OnNodePositionsChanged()
    {
        if (_isDraggingNodeInternal) return; // 拖拽中由 OnNodePanUpdated 统一刷新
        RefreshConnections();
        ScheduleMinimapUpdate();
    }

    private void UpdateMinimap()
    {
        if (_viewModel.Nodes.Count == 0 || CanvasArea.Width < 1)
        {
            MinimapBorder.IsVisible = false;
            return;
        }

        MinimapBorder.IsVisible = true;

        const double minimapW = 180;
        const double minimapH = 120;
        const double nodeW = NodeViewModel.ApproxWidth;
        const double nodeH = NodeViewModel.ApproxHeight;
        const double padding = 50;

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var node in _viewModel.Nodes)
        {
            minX = Math.Min(minX, node.X);
            minY = Math.Min(minY, node.Y);
            maxX = Math.Max(maxX, node.X + nodeW);
            maxY = Math.Max(maxY, node.Y + nodeH);
        }

        // 包含视口
        double viewMinX = -_viewModel.PanX / _viewModel.Zoom;
        double viewMinY = -_viewModel.PanY / _viewModel.Zoom;
        double viewMaxX = viewMinX + CanvasArea.Width / _viewModel.Zoom;
        double viewMaxY = viewMinY + CanvasArea.Height / _viewModel.Zoom;

        minX = Math.Min(minX, viewMinX) - padding;
        minY = Math.Min(minY, viewMinY) - padding;
        maxX = Math.Max(maxX, viewMaxX) + padding;
        maxY = Math.Max(maxY, viewMaxY) + padding;

        var contentW = maxX - minX;
        var contentH = maxY - minY;
        if (contentW < 1 || contentH < 1) return;

        var scale = Math.Min(minimapW / contentW, minimapH / contentH);
        var offsetX = (minimapW - contentW * scale) / 2;
        var offsetY = (minimapH - contentH * scale) / 2;

        _minimapDrawable.Update(_viewModel.Nodes.ToList(), _viewModel.Zoom,
            _viewModel.PanX, _viewModel.PanY,
            CanvasArea.Width, CanvasArea.Height,
            scale, minX, minY, offsetX, offsetY);
        MinimapView.Invalidate();
    }

    /// <summary>点击小地图：将点击处对应的画布点平移到视口中心。</summary>
    private void OnMinimapTapped(object? sender, TappedEventArgs e)
    {
        var pos = e.GetPosition(MinimapView);
        if (pos == null) return;

        var canvas = _minimapDrawable.MinimapToCanvas((float)pos.Value.X, (float)pos.Value.Y);
        if (canvas == null) return;

        _viewModel.PanX = _viewModel.Zoom * (CanvasArea.Width / 2.0 - canvas.Value.X);
        _viewModel.PanY = _viewModel.Zoom * (CanvasArea.Height / 2.0 - canvas.Value.Y);
        // PanX/PanY 变化通过 OnViewModelPropertyChanged → ApplyCanvasTransform() 生效
    }

    // ════════════════ 日志 ════════════════

    private void OnLogAdded(LogEntry entry)
    {
        Dispatcher.Dispatch(() =>
        {
            var time = entry.Timestamp.ToString("HH:mm:ss.fff");
            var node = string.IsNullOrEmpty(entry.NodeName) ? "" : $" [{entry.NodeName}]";
            LogOutput.Text += $"{time}{node}  {entry.Message}\n";
            // 自动滚动到底部：把光标移到文本末尾（WinUI TextBox 会随光标滚动）
            if (_logTextBox != null)
            {
                _logTextBox.SelectionStart = _logTextBox.Text.Length;
                _logTextBox.SelectionLength = 0;
            }
        });
    }

    private void OnLogCleared()
    {
        Dispatcher.Dispatch(() => LogOutput.Text = "");
    }

    private void ClearLog_Click(object? sender, EventArgs e)
    {
        ExecutionLogger.Clear();
    }

    private void LogHeader_Tapped(object? sender, TappedEventArgs e)
    {
        _isLogCollapsed = !_isLogCollapsed;
        LogOutput.IsVisible = !_isLogCollapsed;
        LogToggleIndicator.Text = _isLogCollapsed ? "▲" : "▼";

        if (_isLogCollapsed)
        {
            RootGrid.RowDefinitions[1].Height = GridLength.Auto;
        }
        else
        {
            RootGrid.RowDefinitions[1].Height = new GridLength(150);
        }
    }

    // ════════════════ 侧栏折叠 ════════════════

    private void CollapseSidebar_Click(object? sender, EventArgs e)
    {
        FileSidebar.IsVisible = false;
        ExpandSidebarBtn.IsVisible = true;
        RootGrid.ColumnDefinitions[0].Width = new GridLength(0);
    }

    private void ExpandSidebar_Click(object? sender, EventArgs e)
    {
        RootGrid.ColumnDefinitions[0].Width = new GridLength(220);
        FileSidebar.IsVisible = true;
        ExpandSidebarBtn.IsVisible = false;
    }

    private void CollapseProperty_Click(object? sender, EventArgs e)
    {
        PropertyPanel.IsVisible = false;
        ExpandPropertyBtn.IsVisible = true;
        RootGrid.ColumnDefinitions[2].Width = new GridLength(0);
    }

    private void ExpandProperty_Click(object? sender, EventArgs e)
    {
        RootGrid.ColumnDefinitions[2].Width = new GridLength(270);
        PropertyPanel.IsVisible = true;
        ExpandPropertyBtn.IsVisible = false;
    }

    // ════════════════ 菜单事件 ════════════════

    private void ShowMenu(object? sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        ShowMenuForButton(btn);
    }

    private void ShowMenuForButton(Button btn)
    {
        if (btn.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement el) return;

        // 关闭当前已打开的菜单
        _currentMenuFlyout?.Hide();

        var menu = new Microsoft.UI.Xaml.Controls.MenuFlyout();
        var text = btn.Text ?? "";

        if (text.StartsWith("文件"))
        {
            menu.Items.Add(CreateNativeMenuItem("打开文件夹  Ctrl+O", () => OpenFolder_Click(null, EventArgs.Empty)));
            menu.Items.Add(CreateNativeMenuItem("新建  Ctrl+N", () => NewGraph_Click(null, EventArgs.Empty)));
            menu.Items.Add(CreateNativeMenuItem("保存  Ctrl+S", () => Save_Click(null, EventArgs.Empty)));
            menu.Items.Add(CreateNativeMenuItem("导出", () => Export_Click(null, EventArgs.Empty)));
            menu.Items.Add(CreateNativeMenuItem("导入", () => Import_Click(null, EventArgs.Empty)));
        }
        else if (text.StartsWith("编辑"))
        {
            menu.Items.Add(CreateNativeMenuItem("复制  Ctrl+C", () => Copy_Click(null, EventArgs.Empty)));
            menu.Items.Add(CreateNativeMenuItem("粘贴  Ctrl+V", () => Paste_Click(null, EventArgs.Empty)));
            menu.Items.Add(CreateNativeMenuItem("删除  Del", () => Delete_Click(null, EventArgs.Empty)));
        }
        else if (text.StartsWith("节点"))
        {
            menu.Items.Add(CreateNativeMenuItem("添加节点", () => AddNode_Click(null, EventArgs.Empty)));
            menu.Items.Add(CreateNativeMenuItem("清空", () => Clear_Click(null, EventArgs.Empty)));
        }
        else if (text.StartsWith("视图"))
        {
            menu.Items.Add(CreateNativeMenuItem("全屏  F11", () => ToggleFullScreen_Click(null, EventArgs.Empty)));
        }

        // 追踪菜单状态（用 generation 防止旧菜单的异步 Closed 事件破坏新菜单状态）
        var gen = ++_menuGeneration;
        _currentMenuFlyout = menu;
        _currentMenuButton = btn;
        menu.Closed += (_, _) =>
        {
            if (gen != _menuGeneration) return; // 旧菜单的 Closed，忽略
            _menuHoverTimer?.Stop();
            _currentMenuFlyout = null;
            _currentMenuButton = null;
        };

        var height = btn.Height > 0 ? btn.Height : 32;
        menu.ShowAt(el, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
        {
            Position = new Windows.Foundation.Point(0, height)
        });

        // 启动定时器：菜单打开期间轮询光标位置，鼠标移到其他菜单按钮时自动切换
        _menuHoverTimer?.Stop();
        _menuHoverTimer = Dispatcher.CreateTimer();
        _menuHoverTimer.Interval = TimeSpan.FromMilliseconds(50);
        _menuHoverTimer.Tick += OnMenuHoverTimerTick;
        _menuHoverTimer.Start();
    }

    private static Microsoft.UI.Xaml.Controls.MenuFlyoutItem CreateNativeMenuItem(string text, Action action)
    {
        var item = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    private void OpenFolder_Click(object? sender, EventArgs e)
    {
        _viewModel.FileExplorer.OpenFolder();
    }

    private void NewGraph_Click(object? sender, EventArgs e)
    {
        _viewModel.FileExplorer.CreateNewGraph();
    }

    private async void Save_Click(object? sender, EventArgs e)
    {
        try { await Save_Click_Async(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Save] 异常: {ex.Message}"); }
    }

    private void Export_Click(object? sender, EventArgs e)
    {
        _viewModel.Export();
    }

    private void Import_Click(object? sender, EventArgs e)
    {
        _viewModel.Import();
    }

    private void Clear_Click(object? sender, EventArgs e)
    {
        _viewModel.ClearCommand.Execute(null);
    }

    private void Copy_Click(object? sender, EventArgs e)
    {
        _viewModel.CopySelectedNodes();
        var count = _viewModel.GetSelectedNodes().Count;
        if (count > 0) UpdateStatus($"已复制 {count} 个节点");
        else UpdateStatus("未选中任何节点");
    }

    private void Paste_Click(object? sender, EventArgs e)
    {
        _viewModel.PasteNodes(30, 30);
        UpdateStatus("已粘贴节点");
    }

    private void Delete_Click(object? sender, EventArgs e)
    {
        var selected = _viewModel.GetSelectedNodes();
        var count = selected.Count;
        _viewModel.DeleteSelectedNode();
        UpdateStatus(count > 0 ? $"已删除 {count} 个节点" : "未选中任何节点");
    }

    private void AddNode_Click(object? sender, EventArgs e)
    {
        // 在画布中心弹出搜索窗口
        var centerX = CanvasArea.Width / 2;
        var centerY = CanvasArea.Height / 2;
        ShowNodeSearchWindow(new Point(centerX, centerY));
    }

    // ════════════════ 文件对话框（WinUI 原生） ════════════════

    private async Task<string?> RequestSavePathAsync()
    {
        try
        {
            var hwnd = GetActiveWindowHandle();
            var savePicker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);
            savePicker.SuggestedFileName = "graph";
            savePicker.FileTypeChoices.Add("JSON 文件", new List<string> { ".json" });
            var file = await savePicker.PickSaveFileAsync();
            return file?.Path;
        }
        catch
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "graph_export.json");
        }
    }

    private async Task<string?> RequestOpenPathAsync()
    {
        try
        {
            var hwnd = GetActiveWindowHandle();
            var openPicker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hwnd);
            openPicker.FileTypeFilter.Add(".json");
            var file = await openPicker.PickSingleFileAsync();
            return file?.Path;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> RequestFolderSelectionAsync()
    {
        try
        {
            var hwnd = GetActiveWindowHandle();
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
            folderPicker.FileTypeFilter.Add("*");
            // 起始位置设为桌面（真实文件系统路径），避免虚拟位置导致无法选中文件夹
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            var folder = await folderPicker.PickSingleFolderAsync();
            return folder?.Path;
        }
        catch
        {
            return null;
        }
    }

    private IntPtr GetActiveWindowHandle()
    {
        var window = this.Window?.Handler?.PlatformView;
        if (window is Microsoft.UI.Xaml.Window winuiWindow)
            return WinRT.Interop.WindowNative.GetWindowHandle(winuiWindow);
        return IntPtr.Zero;
    }

    private async Task<string?> RequestNewFileName()
    {
        var name = await DisplayPromptAsync(
            "新建图",
            "输入文件名：",
            accept: "创建",
            cancel: "取消",
            placeholder: "my_graph",
            maxLength: 100,
            keyboard: Keyboard.Text,
            initialValue: "");
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    private Task<bool> RequestOverwriteConfirmation(string filePath)
    {
        var fileName = System.IO.Path.GetFileName(filePath);
        return DisplayAlertAsync("文件已存在", $"“{fileName}” 已存在，是否覆盖？", "覆盖", "取消");
    }

    // ════════════════ ViewModel 属性变化 ════════════════

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.Zoom) or
            nameof(EditorViewModel.PanX) or
            nameof(EditorViewModel.PanY))
        {
            ApplyCanvasTransform();
        }
    }

    // ════════════════ 辅助 ════════════════

    private void UpdateStatus(string text)
    {
        if (_titleBarStatusText != null)
            _titleBarStatusText.Text = text;
    }

    private static Color ParseColor(string hex)
    {
        try { return Color.FromArgb(hex); }
        catch { return Colors.Gray; }
    }
}

namespace NodeEditor.Controls;

/// <summary>
/// 画布网格背景绘制器 — 细线 20px 间隔，粗线 100px 间隔。
/// </summary>
public class GridBackgroundDrawable : IDrawable
{
    public float Zoom { get; set; } = 1f;
    public float PanX { get; set; }
    public float PanY { get; set; }

    /// <summary>画布视口宽度的一半（用于中心锚点补偿）</summary>
    public float CenterX { get; set; }

    /// <summary>画布视口高度的一半</summary>
    public float CenterY { get; set; }

    private const float GridSize = 20f;
    private const float MajorSize = 100f;

    /// <summary>将画布逻辑坐标 X 转换为屏幕坐标 X（含中心锚点补偿）</summary>
    private float ToScreenX(float canvasX) => canvasX * Zoom + CenterX * (1 - Zoom) + PanX;

    /// <summary>将画布逻辑坐标 Y 转换为屏幕坐标 Y</summary>
    private float ToScreenY(float canvasY) => canvasY * Zoom + CenterY * (1 - Zoom) + PanY;

    /// <summary>将屏幕坐标 X 转换为画布逻辑坐标 X</summary>
    private float ToCanvasX(float screenX) => (screenX - PanX - CenterX * (1 - Zoom)) / Zoom;

    /// <summary>将屏幕坐标 Y 转换为画布逻辑坐标 Y</summary>
    private float ToCanvasY(float screenY) => (screenY - PanY - CenterY * (1 - Zoom)) / Zoom;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = Color.FromArgb("#1E1E1E");
        canvas.FillRectangle(dirtyRect);

        var minorColor = Color.FromArgb("#2A2A2A");
        var majorColor = Color.FromArgb("#333333");

        // 计算可见区域在画布逻辑坐标下的范围
        var startX = ToCanvasX(dirtyRect.Left);
        var startY = ToCanvasY(dirtyRect.Top);
        var endX = ToCanvasX(dirtyRect.Right);
        var endY = ToCanvasY(dirtyRect.Bottom);

        // 对齐到网格
        var gridStartX = (int)(startX / GridSize) * GridSize;
        var gridStartY = (int)(startY / GridSize) * GridSize;

        // 细线
        canvas.StrokeColor = minorColor;
        canvas.StrokeSize = 0.5f;

        for (var x = gridStartX; x <= endX; x += GridSize)
        {
            if (Math.Abs(x % MajorSize) < 0.1f) continue; // 跳过粗线位置
            var sx = ToScreenX(x);
            canvas.DrawLine(sx, dirtyRect.Top, sx, dirtyRect.Bottom);
        }

        for (var y = gridStartY; y <= endY; y += GridSize)
        {
            if (Math.Abs(y % MajorSize) < 0.1f) continue;
            var sy = ToScreenY(y);
            canvas.DrawLine(dirtyRect.Left, sy, dirtyRect.Right, sy);
        }

        // 粗线
        canvas.StrokeColor = majorColor;
        canvas.StrokeSize = 0.8f;

        var majorStartX = (int)(startX / MajorSize) * MajorSize;
        var majorStartY = (int)(startY / MajorSize) * MajorSize;

        for (var x = majorStartX; x <= endX; x += MajorSize)
        {
            var sx = ToScreenX(x);
            canvas.DrawLine(sx, dirtyRect.Top, sx, dirtyRect.Bottom);
        }

        for (var y = majorStartY; y <= endY; y += MajorSize)
        {
            var sy = ToScreenY(y);
            canvas.DrawLine(dirtyRect.Left, sy, dirtyRect.Right, sy);
        }
    }
}
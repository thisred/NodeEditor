using System;
using System.Collections.Generic;
using NodeEditor.ViewModels;

namespace NodeEditor.Controls;

/// <summary>
/// 连线绘制器 — 在 GraphicsView 上绘制贝塞尔曲线连线。
/// GraphicsView 位于 CanvasContainer 外部（与 GridBackground 同级），
/// 通过手动坐标变换（Pan/Zoom）将画布坐标映射到屏幕坐标。
/// 这样连线渲染区域始终覆盖整个视口，不受 CanvasContainer 尺寸限制。
/// </summary>
public class ConnectionDrawable : IDrawable
{
    public List<ConnectionViewModel> Connections { get; set; } = new();

    // ── 画布变换参数（与 CanvasContainer 的 Translation/Scale 同步） ──
    public float Zoom { get; set; } = 1f;
    public float PanX { get; set; }
    public float PanY { get; set; }
    public float CenterX { get; set; }
    public float CenterY { get; set; }

    // ── 临时连线（拖拽中） ──
    public bool HasPending { get; set; }
    public float PendingX1 { get; set; }
    public float PendingY1 { get; set; }
    public float PendingX2 { get; set; }
    public float PendingY2 { get; set; }

    // 画布坐标 → 屏幕坐标（与 GridBackgroundDrawable 相同公式）
    private float ToScreenX(float canvasX) => canvasX * Zoom + CenterX * (1 - Zoom) + PanX;
    private float ToScreenY(float canvasY) => canvasY * Zoom + CenterY * (1 - Zoom) + PanY;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        // 绘制已有连线
        canvas.StrokeColor = Color.FromArgb("#569CD6");
        canvas.StrokeSize = 2.5f * Zoom;
        canvas.StrokeLineCap = LineCap.Round;

        foreach (var conn in Connections)
        {
            var sx1 = ToScreenX((float)conn.X1);
            var sy1 = ToScreenY((float)conn.Y1);
            var sx2 = ToScreenX((float)conn.X2);
            var sy2 = ToScreenY((float)conn.Y2);
            DrawBezier(canvas, sx1, sy1, sx2, sy2);
        }

        // 绘制临时连线（虚线）
        if (HasPending)
        {
            canvas.StrokeColor = Color.FromArgb("#FFD700");
            canvas.StrokeSize = 2f * Zoom;
            canvas.StrokeDashPattern = new[] { 4f, 2f };
            var px1 = ToScreenX(PendingX1);
            var py1 = ToScreenY(PendingY1);
            var px2 = ToScreenX(PendingX2);
            var py2 = ToScreenY(PendingY2);
            DrawBezier(canvas, px1, py1, px2, py2);
            canvas.StrokeDashPattern = null;
        }
    }

    private static void DrawBezier(ICanvas canvas, float x1, float y1, float x2, float y2)
    {
        var midX = (x1 + x2) / 2f;
        var path = new PathF();
        path.MoveTo(x1, y1);
        path.CurveTo(midX, y1, midX, y2, x2, y2);
        canvas.DrawPath(path);
    }

    /// <summary>
    /// 命中测试：查找离指定画布坐标最近的连线。
    /// hitRadius 为画布坐标容差。
    /// </summary>
    public ConnectionViewModel? HitTest(double px, double py, double hitRadius = 12)
    {
        ConnectionViewModel? best = null;
        var bestDist = hitRadius;

        foreach (var conn in Connections)
        {
            var dist = DistanceToBezier(px, py,
                conn.X1, conn.Y1, conn.X2, conn.Y2);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = conn;
            }
        }
        return best;
    }

    /// <summary>
    /// 近似计算点到三次贝塞尔曲线的最小距离（采样 20 段）。
    /// </summary>
    private static double DistanceToBezier(double px, double py,
        double x1, double y1, double x2, double y2)
    {
        var midX = (x1 + x2) / 2.0;
        double minDist = double.MaxValue;
        double prevX = x1, prevY = y1;

        const int steps = 20;
        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            double u = 1 - t;
            double bx = u * u * u * x1 + 3 * u * u * t * midX + 3 * u * t * t * midX + t * t * t * x2;
            double by = u * u * u * y1 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y2;

            double mx = (bx + prevX) / 2;
            double my = (by + prevY) / 2;
            double dx = mx - px;
            double dy = my - py;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d < minDist) minDist = d;

            prevX = bx;
            prevY = by;
        }
        return minDist;
    }
}

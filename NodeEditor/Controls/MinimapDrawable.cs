using System;
using System.Collections.Generic;
using NodeEditor.ViewModels;

namespace NodeEditor.Controls;

/// <summary>
/// 小地图绘制器 — 绘制节点缩略图和视口矩形。
/// </summary>
public class MinimapDrawable : IDrawable
{
    private List<NodeViewModel> _nodes = new();
    private double _zoom = 1;
    private double _panX, _panY;
    private double _canvasW, _canvasH;
    private double _scale, _minX, _minY, _offsetX, _offsetY;

    private const double NodeW = NodeViewModel.ApproxWidth;
    private const double NodeH = NodeViewModel.ApproxHeight;

    public void Update(List<NodeViewModel> nodes, double zoom, double panX, double panY,
        double canvasW, double canvasH,
        double scale, double minX, double minY, double offsetX, double offsetY)
    {
        _nodes = nodes;
        _zoom = zoom;
        _panX = panX;
        _panY = panY;
        _canvasW = canvasW;
        _canvasH = canvasH;
        _scale = scale;
        _minX = minX;
        _minY = minY;
        _offsetX = offsetX;
        _offsetY = offsetY;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        // 背景
        canvas.FillColor = Color.FromArgb("#252526");
        canvas.FillRectangle(dirtyRect);

        if (_nodes.Count == 0 || _scale == 0) return;

        // 绘制节点
        foreach (var node in _nodes)
        {
            var x = (float)(_offsetX + (node.X - _minX) * _scale);
            var y = (float)(_offsetY + (node.Y - _minY) * _scale);
            var w = (float)Math.Max(2, NodeW * _scale);
            var h = (float)Math.Max(2, NodeH * _scale);

            canvas.FillColor = ParseColorSafe(node.Color);
            canvas.FillRectangle(x, y, w, h);
        }

        // 绘制视口矩形
        var viewMinX = -_panX / _zoom;
        var viewMinY = -_panY / _zoom;
        var viewMaxX = viewMinX + _canvasW / _zoom;
        var viewMaxY = viewMinY + _canvasH / _zoom;

        var vx = (float)(_offsetX + (viewMinX - _minX) * _scale);
        var vy = (float)(_offsetY + (viewMinY - _minY) * _scale);
        var vw = (float)Math.Max(1, (viewMaxX - viewMinX) * _scale);
        var vh = (float)Math.Max(1, (viewMaxY - viewMinY) * _scale);

        canvas.StrokeColor = Colors.White.WithAlpha(0.7f);
        canvas.StrokeSize = 1.5f;
        canvas.DrawRectangle(vx, vy, vw, vh);
    }

    private static Color ParseColorSafe(string hex)
    {
        try
        {
            return Color.FromArgb(hex).WithAlpha(0.8f);
        }
        catch
        {
            return Colors.Gray.WithAlpha(0.8f);
        }
    }

    /// <summary>
    /// 将小地图局部坐标（相对 MinimapView）转换为画布逻辑坐标。
    /// 尚无内容（未计算比例）时返回 null。
    /// </summary>
    public PointF? MinimapToCanvas(float localX, float localY)
    {
        if (_scale == 0) return null;
        var canvasX = _minX + (localX - _offsetX) / _scale;
        var canvasY = _minY + (localY - _offsetY) / _scale;
        return new PointF((float)canvasX, (float)canvasY);
    }
}
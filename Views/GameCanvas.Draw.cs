using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVATrek.Models;

namespace AVATrek.Views;

// Drawing primitives shared by every part of the screen.
public partial class GameCanvas
{
    private static readonly Color TextColor = Color.FromRgb(120, 200, 255);
    private static readonly Color Gold = Color.FromRgb(255, 210, 90);
    private static readonly Color AlertRed = Color.FromRgb(255, 70, 60);
    private static readonly Color ShipColor = Color.FromRgb(225, 240, 255);
    private static readonly Color PhaserColor = Color.FromRgb(255, 120, 60);
    private static readonly Color TorpedoColor = Color.FromRgb(255, 230, 170);
    private static readonly Color DisruptorColor = Color.FromRgb(120, 255, 110);
    private static readonly Color FrameColor = Color.FromRgb(70, 140, 220);

    private static readonly Typeface HudFont = new(new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"), FontStyle.Normal, FontWeight.Bold);

    private readonly Random _random = new();

    // Pens are cached by colour, opacity and width: a frame draws a couple of thousand lines.
    private readonly Dictionary<(Color, byte, double), IPen> _pens = new();
    private readonly Dictionary<(Color, byte), IBrush> _brushes = new();

    private double Clock => _engine.Clock;

    private bool Blink(double perSecond) => (int)(Clock * perSecond) % 2 == 0;

    // A vector-monitor look: each line is drawn once wide and faint, then again thin and bright.
    private void DrawGlowLine(DrawingContext context, Point a, Point b, Color color, double alpha, double width)
    {
        if (alpha <= 0.01) return;
        context.DrawLine(GetPen(color, alpha * 0.22, Math.Round(width * 3.5, 1)), a, b);
        context.DrawLine(GetPen(color, alpha, width), a, b);
    }

    private void DrawGlowPolygon(DrawingContext context, IReadOnlyList<Point> points, Color color, double alpha, double width)
    {
        for (int i = 0; i < points.Count; i++)
            DrawGlowLine(context, points[i], points[(i + 1) % points.Count], color, alpha, width);
    }

    private void DrawFrame(DrawingContext context, Rect rect, Color color, double alpha = 0.7)
    {
        DrawGlowPolygon(context, new[] { rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft }, color, alpha, 1.5);
    }

    private IPen GetPen(Color color, double alpha, double width)
    {
        byte a = (byte)Math.Clamp(alpha * 255, 0, 255);
        // Widths are rounded so distance-scaled lines don't fill the cache with near-duplicates.
        width = Math.Round(width * 4) / 4;
        var key = (color, a, width);
        if (!_pens.TryGetValue(key, out var pen))
        {
            pen = new Pen(GetBrush(color, alpha), width, lineCap: PenLineCap.Round);
            if (_pens.Count > 4000) _pens.Clear();
            _pens[key] = pen;
        }
        return pen;
    }

    private IBrush GetBrush(Color color, double alpha)
    {
        byte a = (byte)Math.Clamp(alpha * 255, 0, 255);
        var key = (color, a);
        if (!_brushes.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B));
            if (_brushes.Count > 2000) _brushes.Clear();
            _brushes[key] = brush;
        }
        return brush;
    }

    private static Color Lerp(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(from.R + (to.R - from.R) * t),
            (byte)(from.G + (to.G - from.G) * t),
            (byte)(from.B + (to.B - from.B) * t));
    }

    private static FormattedText Text(string text, double size, Color color, double alpha = 1) => new(
        text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, HudFont, size,
        new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(alpha, 0, 1)), color.R, color.G, color.B)));

    private static void DrawCentredText(DrawingContext context, string text, double size, double y, Color color,
        double centreX = ScreenWidth / 2, double alpha = 1)
    {
        var formatted = Text(text, size, color, alpha);
        context.DrawText(formatted, new Point(centreX - formatted.Width / 2, y));
    }

    private static void DrawRightText(DrawingContext context, string text, double size, double right, double y, Color color)
    {
        var formatted = Text(text, size, color);
        context.DrawText(formatted, new Point(right - formatted.Width, y));
    }

    // A dark panel over the screen, for the pause, entry and table screens.
    private void DrawPanel(DrawingContext context, Rect rect)
    {
        context.FillRectangle(GetBrush(Colors.Black, 0.85), rect);
        DrawFrame(context, rect, Gold, 0.6);
    }

    /// <summary>
    /// Draws a model flat, seen from directly above, as the tactical scanner shows it. Rotation is in
    /// the plane; height is ignored.
    /// </summary>
    private void DrawModelTopDown(DrawingContext context, Wireframe model, Point position, double yaw, double size,
        Color color, double alpha, double width)
    {
        double c = Math.Cos(yaw) * size, s = Math.Sin(yaw) * size;
        foreach (var (a, b) in model.Lines)
        {
            var pa = new Point(position.X + a.X * c - a.Y * s, position.Y + a.X * s + a.Y * c);
            var pb = new Point(position.X + b.X * c - b.Y * s, position.Y + b.X * s + b.Y * c);
            DrawGlowLine(context, pa, pb, color, alpha, width);
        }
    }

    /// <summary>
    /// Draws a model turned and tipped toward the viewer, for the title and legend: yaw spins it,
    /// pitch tips the nose down so the top shows.
    /// </summary>
    private void DrawModelShowcase(DrawingContext context, Wireframe model, Point centre, double size, double yaw,
        double pitch, Color color, double alpha, double width)
    {
        const double cameraDistance = 5;
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);

        Point Project(Vec3 v)
        {
            // Spin about the vertical axis, then look down on it from above and in front.
            double x = v.X * cy - v.Y * sy;       // away from the camera
            double y = v.X * sy + v.Y * cy;       // to the right on screen
            double up = v.Z * cp + x * sp;
            double away = x * cp - v.Z * sp;
            double k = cameraDistance / (cameraDistance + away);
            return new Point(centre.X + y * size * k, centre.Y - up * size * k);
        }

        foreach (var (a, b) in model.Lines)
            DrawGlowLine(context, Project(a), Project(b), color, alpha, width);
    }
}

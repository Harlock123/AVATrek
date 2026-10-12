using System;
using Avalonia;

namespace AVATrek.Models;

/// <summary>
/// The sector: a fixed rectangle of space that wraps at its edges, as the tactical scanner shows it.
/// Distances and directions are always taken the short way round the wrap.
/// </summary>
public static class World
{
    public const double Width = 1200;
    public const double Height = 540;
    public static readonly Point Center = new(Width / 2, Height / 2);

    public static Point Wrap(Point p)
    {
        double x = p.X % Width, y = p.Y % Height;
        if (x < 0) x += Width;
        if (y < 0) y += Height;
        return new Point(x, y);
    }

    /// <summary>The shortest vector from one point to another, across the wrap if that is shorter.</summary>
    public static Vector Delta(Point from, Point to)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y;
        if (dx > Width / 2) dx -= Width;
        else if (dx < -Width / 2) dx += Width;
        if (dy > Height / 2) dy -= Height;
        else if (dy < -Height / 2) dy += Height;
        return new Vector(dx, dy);
    }

    public static double Distance(Point a, Point b) => Delta(a, b).Length;

    public static double AngleTo(Point from, Point to)
    {
        var d = Delta(from, to);
        return Math.Atan2(d.Y, d.X);
    }

    /// <summary>The signed difference between two headings, in the range -π to π.</summary>
    public static double AngleDiff(double target, double current) => Math.IEEERemainder(target - current, Math.PI * 2);

    /// <summary>Turns a heading toward a target heading by at most maxStep.</summary>
    public static double TurnToward(double current, double target, double maxStep)
    {
        double diff = AngleDiff(target, current);
        return current + Math.Clamp(diff, -maxStep, maxStep);
    }
}

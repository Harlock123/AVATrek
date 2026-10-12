using System;
using System.Collections.Generic;

namespace AVATrek.Models;

/// <summary>A point in a model's own space: X forward, Y to starboard (right), Z up.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
}

/// <summary>
/// A vector-display model: nothing but line segments in 3D, roughly a unit in size. The same model is
/// drawn flat from above on the tactical scanner (Z is ignored) and in perspective on the viewer.
/// </summary>
public class Wireframe
{
    public (Vec3 A, Vec3 B)[] Lines { get; }

    private Wireframe(List<(Vec3, Vec3)> lines) => Lines = lines.ToArray();

    // Builder helpers.
    private sealed class Builder
    {
        public readonly List<(Vec3, Vec3)> Lines = new();

        public Builder Line(Vec3 a, Vec3 b)
        {
            Lines.Add((a, b));
            return this;
        }

        public Builder Path(bool closed, params Vec3[] points)
        {
            for (int i = 0; i < points.Length - 1; i++)
                Lines.Add((points[i], points[i + 1]));
            if (closed && points.Length > 2)
                Lines.Add((points[^1], points[0]));
            return this;
        }

        /// <summary>A ring of points in a horizontal plane.</summary>
        public static Vec3[] Ring(Vec3 center, double rx, double ry, int count, double phase = 0)
        {
            var points = new Vec3[count];
            for (int i = 0; i < count; i++)
            {
                double a = phase + i * Math.PI * 2 / count;
                points[i] = new Vec3(center.X + Math.Cos(a) * rx, center.Y + Math.Sin(a) * ry, center.Z);
            }
            return points;
        }

        /// <summary>A ring of points round the X axis (a cross-section of something long).</summary>
        public static Vec3[] Hoop(Vec3 center, double r, int count, double phase = 0)
        {
            var points = new Vec3[count];
            for (int i = 0; i < count; i++)
            {
                double a = phase + i * Math.PI * 2 / count;
                points[i] = new Vec3(center.X, center.Y + Math.Cos(a) * r, center.Z + Math.Sin(a) * r);
            }
            return points;
        }

        /// <summary>A tube along X from x0 to x1: end hoops joined by lengthwise lines.</summary>
        public Builder Tube(double x0, double x1, double y, double z, double r0, double r1, int sides, bool capFront = true)
        {
            var front = Hoop(new Vec3(x0, y, z), r0, sides, Math.PI / sides);
            var back = Hoop(new Vec3(x1, y, z), r1, sides, Math.PI / sides);
            if (capFront) Path(true, front);
            Path(true, back);
            for (int i = 0; i < sides; i++)
                Line(front[i], back[i]);
            return this;
        }

        public Wireframe Build() => new(Lines);
    }

    private static Vec3 V(double x, double y, double z) => new(x, y, z);

    /// <summary>
    /// The Enterprise: the saucer forward, the engineering hull under and behind it on a neck, and the
    /// two warp nacelles up on their pylons. About 2 units long, nose at +1.
    /// </summary>
    public static readonly Wireframe Enterprise = BuildEnterprise();

    private static Wireframe BuildEnterprise()
    {
        var b = new Builder();

        // Saucer: rim, upper deck ring and the bridge dome.
        var rim = Builder.Ring(V(0.42, 0, 0.18), 0.56, 0.56, 18);
        var deck = Builder.Ring(V(0.42, 0, 0.27), 0.30, 0.30, 12);
        var bridge = Builder.Ring(V(0.42, 0, 0.33), 0.09, 0.09, 6);
        b.Path(true, rim).Path(true, deck).Path(true, bridge);
        for (int i = 0; i < 6; i++)
            b.Line(rim[i * 3], deck[i * 2]);

        // Neck down to the engineering hull.
        b.Line(V(0.14, 0.04, 0.16), V(-0.12, 0.04, -0.10));
        b.Line(V(0.02, -0.04, 0.16), V(-0.24, -0.04, -0.10));
        b.Line(V(0.14, -0.04, 0.16), V(-0.12, -0.04, -0.10));
        b.Line(V(0.02, 0.04, 0.16), V(-0.24, 0.04, -0.10));

        // Engineering hull, with the deflector dish at its front.
        b.Tube(0.02, -0.78, 0, -0.14, 0.11, 0.08, 8);
        b.Path(true, Builder.Hoop(V(0.06, 0, -0.14), 0.06, 6));

        // Pylons out to the nacelles.
        foreach (double side in new[] { 1.0, -1.0 })
        {
            b.Line(V(-0.42, side * 0.07, -0.06), V(-0.52, side * 0.44, 0.26));
            b.Line(V(-0.60, side * 0.07, -0.08), V(-0.70, side * 0.44, 0.24));
            b.Tube(-0.22, -1.02, side * 0.44, 0.27, 0.075, 0.06, 6);
        }

        return b.Build();
    }

    /// <summary>
    /// A Klingon battle cruiser: the command pod far forward on a long boom, the swept wings drooping to
    /// the nacelles at their tips. About 2 units long, nose at +1.
    /// </summary>
    public static readonly Wireframe Klingon = BuildKlingon();

    private static Wireframe BuildKlingon()
    {
        var b = new Builder();

        // Command pod and the boom back to the wings.
        var pod = Builder.Ring(V(0.86, 0, 0.02), 0.13, 0.11, 8);
        b.Path(true, pod);
        b.Line(V(0.99, 0, 0.02), V(0.86, 0, 0.10)).Line(V(0.86, 0, 0.10), V(0.73, 0, 0.02));
        b.Line(V(0.73, 0.04, 0.02), V(0.12, 0.06, 0.04));
        b.Line(V(0.73, -0.04, 0.02), V(0.12, -0.06, 0.04));

        // Body and wings, symmetrical, with the wingtips dropping well below the body.
        var hullTop = new[]
        {
            V(0.18, 0, 0.06), V(-0.10, 0.22, 0.04), V(-0.30, 0.76, -0.22), V(-0.60, 0.80, -0.22),
            V(-0.48, 0.22, 0.00), V(-0.62, 0, 0.04), V(-0.48, -0.22, 0.00), V(-0.60, -0.80, -0.22),
            V(-0.30, -0.76, -0.22), V(-0.10, -0.22, 0.04)
        };
        b.Path(true, hullTop);
        b.Line(V(0.18, 0, 0.06), V(-0.62, 0, 0.04));

        // Nacelles at the wingtips.
        foreach (double side in new[] { 1.0, -1.0 })
            b.Tube(-0.12, -0.92, side * 0.80, -0.24, 0.05, 0.05, 4);

        return b.Build();
    }

    /// <summary>An anti-matter saucer: a flat double cone with a dome. Radius 1.</summary>
    public static readonly Wireframe Saucer = BuildSaucer();

    private static Wireframe BuildSaucer()
    {
        var b = new Builder();
        var rim = Builder.Ring(V(0, 0, 0), 1, 1, 10);
        var dome = Builder.Ring(V(0, 0, 0.32), 0.35, 0.35, 10);
        b.Path(true, rim).Path(true, dome);
        for (int i = 0; i < 10; i++)
        {
            b.Line(rim[i], dome[i]);
            if (i % 2 == 0) b.Line(rim[i], V(0, 0, -0.35));
        }
        return b.Build();
    }

    /// <summary>A Federation starbase: a hub with two docking rings on spokes and antennae. Radius 1.</summary>
    public static readonly Wireframe Starbase = BuildStarbase();

    private static Wireframe BuildStarbase()
    {
        var b = new Builder();
        var outer = Builder.Ring(V(0, 0, 0), 1, 1, 16);
        var inner = Builder.Ring(V(0, 0, 0), 0.82, 0.82, 16);
        var hub = Builder.Ring(V(0, 0, 0), 0.26, 0.26, 8);
        b.Path(true, outer).Path(true, inner).Path(true, hub);
        for (int i = 0; i < 16; i += 2)
            b.Line(outer[i], inner[i]);
        for (int i = 0; i < 4; i++)
            b.Line(hub[i * 2], inner[i * 4]);

        // The hub's upper and lower towers.
        b.Path(true, Builder.Ring(V(0, 0, 0.30), 0.14, 0.14, 6));
        b.Path(true, Builder.Ring(V(0, 0, -0.30), 0.18, 0.18, 6));
        b.Line(V(0.26, 0, 0), V(0.14, 0, 0.30)).Line(V(-0.26, 0, 0), V(-0.14, 0, 0.30));
        b.Line(V(0, 0.26, 0), V(0, 0.18, -0.30)).Line(V(0, -0.26, 0), V(0, -0.18, -0.30));
        b.Line(V(0, 0, 0.30), V(0, 0, 0.75));
        b.Line(V(0, 0, -0.30), V(0, 0, -0.60));
        return b.Build();
    }

    /// <summary>Nomad: a squat octagonal probe with a dome and antenna rods. Radius 1.</summary>
    public static readonly Wireframe Nomad = BuildNomad();

    private static Wireframe BuildNomad()
    {
        var b = new Builder();
        var top = Builder.Ring(V(0, 0, 0.30), 0.85, 0.85, 8, Math.PI / 8);
        var bottom = Builder.Ring(V(0, 0, -0.30), 0.85, 0.85, 8, Math.PI / 8);
        var dome = Builder.Ring(V(0, 0, 0.62), 0.42, 0.42, 8, Math.PI / 8);
        var foot = Builder.Ring(V(0, 0, -0.55), 0.40, 0.40, 8, Math.PI / 8);
        b.Path(true, top).Path(true, bottom).Path(true, dome).Path(true, foot);
        for (int i = 0; i < 8; i++)
        {
            b.Line(top[i], bottom[i]);
            b.Line(top[i], dome[i]);
            b.Line(bottom[i], foot[i]);
        }

        // The sensor rods sticking out ahead and to the sides.
        b.Line(V(0.85, 0, 0.10), V(1.35, 0, 0.18));
        b.Line(V(0, 0.85, 0.10), V(0, 1.25, 0.30));
        b.Line(V(0, -0.85, 0.10), V(0, -1.25, 0.30));
        b.Line(V(0, 0, 0.62), V(0, 0, 1.05));
        return b.Build();
    }

    /// <summary>A space mine: spikes along all three axes round a small core. Radius 1.</summary>
    public static readonly Wireframe Mine = BuildMine();

    private static Wireframe BuildMine()
    {
        var b = new Builder();
        b.Path(true, Builder.Ring(V(0, 0, 0), 0.4, 0.4, 6));
        b.Path(true, Builder.Hoop(V(0, 0, 0), 0.4, 6));
        var spikes = new[] { V(1, 0, 0), V(-1, 0, 0), V(0, 1, 0), V(0, -1, 0), V(0, 0, 1), V(0, 0, -1),
                             V(0.6, 0.6, 0), V(-0.6, -0.6, 0), V(0.6, -0.6, 0), V(-0.6, 0.6, 0) };
        foreach (var s in spikes)
            b.Line(s * 0.35, s);
        return b.Build();
    }
}

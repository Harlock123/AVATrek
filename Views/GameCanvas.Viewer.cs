using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AVATrek.Engine;
using AVATrek.Models;

namespace AVATrek.Views;

// The main viewer: the sector seen from the bridge, looking out over the bow in perspective.
public partial class GameCanvas
{
    private static readonly Rect ViewerRect = new(340, 12, 672, 290);
    private const double FieldOfView = 64 * Math.PI / 180;
    private const double NearPlane = 6;
    private const double ViewRange = 760;
    private const double CameraHeight = 0;
    private const double ModelTilt = 0.5;

    private static readonly double Focal = ViewerRect.Width / 2 / Math.Tan(FieldOfView / 2);
    private static Point ViewerCentre => new(ViewerRect.X + ViewerRect.Width / 2, ViewerRect.Y + ViewerRect.Height / 2);

    // Distant stars, fixed in direction: they only move as the ship turns.
    private readonly List<(double Azimuth, double Elevation, double Brightness)> _farStars = new();

    // Specks of dust close by, fixed in the sector: they stream past as the ship moves.
    private readonly List<Vec3> _dust = new();

    private void InitViewer()
    {
        for (int i = 0; i < 260; i++)
            _farStars.Add((_random.NextDouble() * Math.PI * 2, (_random.NextDouble() - 0.5) * 0.65, 0.25 + _random.NextDouble() * 0.75));
    }

    private void UpdateViewer(double deltaTime)
    {
        if (!_engine.IsInPlay && _engine.State != GameState.GameOver) return;

        // Keep a cloud of dust round the ship; specks left behind or far off are put back ahead.
        var ship = _engine.Ship;
        for (int i = _dust.Count - 1; i >= 0; i--)
        {
            var d = World.Delta(ship.Position, new Point(_dust[i].X, _dust[i].Y));
            double fwd = d.X * ship.Heading.X + d.Y * ship.Heading.Y;
            if (d.Length > 320 || fwd < -20)
                _dust.RemoveAt(i);
        }
        while (_dust.Count < 70)
        {
            double a = ship.Rotation + (_random.NextDouble() - 0.5) * 1.6;
            double r = 60 + _random.NextDouble() * 250;
            var p = World.Wrap(ship.Position + new Vector(Math.Cos(a), Math.Sin(a)) * r);
            _dust.Add(new Vec3(p.X, p.Y, (_random.NextDouble() - 0.5) * 120));
        }
    }

    /// <summary>A point in the sector, as the viewer sees it: screen position and distance ahead.</summary>
    private readonly struct Viewed
    {
        public readonly double Forward, Side, Up;

        public Viewed(double forward, double side, double up)
        {
            Forward = forward;
            Side = side;
            Up = up;
        }

        public Point Screen => new(ViewerCentre.X + Side / Forward * Focal, ViewerCentre.Y - Up / Forward * Focal);
    }

    private Viewed ToCamera(Vector offset, double height)
    {
        double rot = _engine.Ship.Rotation;
        double c = Math.Cos(rot), s = Math.Sin(rot);
        return new Viewed(offset.X * c + offset.Y * s, -offset.X * s + offset.Y * c, height - CameraHeight);
    }

    /// <summary>Draws a line in the sector through the camera, trimmed where it passes behind the viewer.</summary>
    private void DrawViewedLine(DrawingContext context, Viewed a, Viewed b, Color color, double alpha, double width)
    {
        if (a.Forward < NearPlane && b.Forward < NearPlane) return;
        if (a.Forward < NearPlane || b.Forward < NearPlane)
        {
            double t = (NearPlane - a.Forward) / (b.Forward - a.Forward);
            var clipped = new Viewed(NearPlane, a.Side + (b.Side - a.Side) * t, a.Up + (b.Up - a.Up) * t);
            if (a.Forward < NearPlane) a = clipped;
            else b = clipped;
        }
        DrawGlowLine(context, a.Screen, b.Screen, color, alpha, width);
    }

    private static double DistanceFade(double distance) => Math.Clamp(1 - (distance - 420) / (ViewRange - 420), 0, 1);

    private void DrawViewedModel(DrawingContext context, GameObject obj, IModelled modelled, Color color, double alpha)
    {
        var centre = World.Delta(_engine.Ship.Position, obj.Position);
        double distance = centre.Length;
        if (distance > ViewRange) return;
        alpha *= DistanceFade(distance);

        double size = modelled.ModelSize, yaw = modelled.ModelYaw;
        double c = Math.Cos(yaw) * size, s = Math.Sin(yaw) * size;
        double width = Math.Clamp(260 / Math.Max(distance, 1), 1.0, 2.2);

        // Everything sits on the bridge's own level, so seen straight on a model would be edge-on and
        // flat. Each one is tipped toward the viewer instead, as if seen a little from above.
        double centreForward = ToCamera(centre, 0).Forward;
        Viewed Tipped(Vector offset, double height)
        {
            var v = ToCamera(offset, height);
            return new Viewed(v.Forward, v.Side, v.Up + (v.Forward - centreForward) * ModelTilt);
        }

        foreach (var (a, b) in modelled.Model.Lines)
        {
            var oa = new Vector(centre.X + a.X * c - a.Y * s, centre.Y + a.X * s + a.Y * c);
            var ob = new Vector(centre.X + b.X * c - b.Y * s, centre.Y + b.X * s + b.Y * c);
            DrawViewedLine(context, Tipped(oa, obj.Altitude + a.Z * size), Tipped(ob, obj.Altitude + b.Z * size),
                color, alpha, width);
        }
    }

    private void DrawViewer(DrawingContext context)
    {
        context.FillRectangle(GetBrush(Color.FromRgb(2, 4, 12), 1), ViewerRect);

        using (context.PushClip(ViewerRect))
        {
            DrawViewerStars(context);
            DrawViewerDust(context);
            DrawViewerObjects(context);
            DrawViewerPhasers(context);
            DrawCrosshair(context);
            DrawViewerOverlays(context);
        }

        // The frame flashes red when the shields take a hit.
        var frame = _engine.HitFlash > 0 ? Lerp(FrameColor, AlertRed, _engine.HitFlash * 4) : FrameColor;
        DrawFrame(context, ViewerRect, frame, 0.8);
        context.DrawText(Text("MAIN VIEWER", 12, TextColor, 0.7), new Point(ViewerRect.X + 8, ViewerRect.Y + 5));
        int heading = ((int)Math.Round(_engine.Ship.Rotation * 180 / Math.PI + 90) % 360 + 360) % 360;
        DrawRightText(context, $"HEADING {heading:000}", 12, ViewerRect.Right - 8, ViewerRect.Y + 5, TextColor);
    }

    private void DrawViewerStars(DrawingContext context)
    {
        bool warping = _engine.ShipAlive && _engine.Ship.IsWarping;
        var centre = ViewerCentre;
        foreach (var (azimuth, elevation, brightness) in _farStars)
        {
            double diff = World.AngleDiff(azimuth, _engine.Ship.Rotation);
            if (Math.Abs(diff) > FieldOfView / 2 + 0.05) continue;
            var p = new Point(centre.X + Math.Tan(diff) * Focal, centre.Y - Math.Tan(elevation) * Focal);

            if (warping)
            {
                // At warp the stars stretch away from the centre of the view.
                var outward = p - centre;
                DrawGlowLine(context, p, p + outward * 0.35, Color.FromRgb(200, 220, 255), brightness, 1.2);
            }
            else
            {
                double twinkle = 0.75 + 0.25 * Math.Sin(Clock * (0.7 + brightness) + azimuth * 13);
                context.FillRectangle(GetBrush(Color.FromRgb(200, 215, 255), brightness * twinkle), new Rect(p.X - 0.8, p.Y - 0.8, 1.6, 1.6));
            }
        }
    }

    private void DrawViewerDust(DrawingContext context)
    {
        var ship = _engine.Ship;
        double speed = ship.Velocity.Length;
        foreach (var speck in _dust)
        {
            var offset = World.Delta(ship.Position, new Point(speck.X, speck.Y));
            var v = ToCamera(offset, speck.Z);
            if (v.Forward < NearPlane) continue;
            double alpha = Math.Clamp(1 - v.Forward / 320, 0, 1) * 0.8;

            // Moving fast, each speck draws a short trail back toward where it was.
            if (speed > 40)
            {
                var back = ToCamera(offset + ship.Velocity * 0.03, speck.Z);
                if (back.Forward > NearPlane)
                {
                    DrawGlowLine(context, v.Screen, back.Screen, Color.FromRgb(160, 180, 220), alpha, 1);
                    continue;
                }
            }
            var p = v.Screen;
            context.FillRectangle(GetBrush(Color.FromRgb(160, 180, 220), alpha), new Rect(p.X - 1, p.Y - 1, 2, 2));
        }
    }

    private void DrawViewerObjects(DrawingContext context)
    {
        var ship = _engine.Ship;

        // Far things first, so near ones draw over them.
        var things = new List<(double Distance, Action Draw)>();
        void Add(GameObject obj, Action draw) => things.Add((World.Distance(ship.Position, obj.Position), draw));

        foreach (var starbase in _engine.Starbases)
        {
            var color = starbase.HitFlash > 0 ? Colors.White : GameEngine.StarbaseColor;
            Add(starbase, () => DrawViewedModel(context, starbase, starbase, color, 1));
        }
        foreach (var klingon in _engine.Klingons)
        {
            double alpha = klingon.HasArrived ? 1 : ArrivalAlpha(klingon);
            var color = klingon.HitFlash > 0 ? Colors.White : GameEngine.KlingonColor;
            Add(klingon, () => DrawViewedModel(context, klingon, klingon, color, alpha));
        }
        foreach (var saucer in _engine.Saucers.Where(s => !s.IsAttached))
        {
            Add(saucer, () => DrawViewedModel(context, saucer, saucer, GameEngine.SaucerColor, 1));
        }
        foreach (var mine in _engine.Mines)
        {
            double alpha = mine.IsFading && Blink(8) ? 0.3 : 1;
            Add(mine, () => DrawViewedModel(context, mine, mine, GameEngine.MineColor, alpha));
        }
        if (_engine.Nomad is { } nomad)
        {
            var color = nomad.HitFlash > 0 ? Colors.White : GameEngine.NomadColor;
            Add(nomad, () => DrawViewedModel(context, nomad, nomad, color, 1));
        }
        foreach (var torpedo in _engine.Torpedoes)
            Add(torpedo, () => DrawViewedGlow(context, torpedo.Position, 0, 7, TorpedoColor, true));
        foreach (var bolt in _engine.Disruptors)
            Add(bolt, () => DrawViewedGlow(context, bolt.Position, 0, 4, DisruptorColor, false));
        foreach (var explosion in _engine.Explosions)
            things.Add((World.Distance(ship.Position, explosion.Position), () => DrawViewedExplosion(context, explosion)));

        foreach (var (_, draw) in things.OrderByDescending(t => t.Distance))
            draw();
    }

    private double ArrivalAlpha(Klingon klingon) => Math.Clamp(1 - klingon.ArrivalTime / 0.8, 0, 1) * (Blink(12) ? 1 : 0.5);

    /// <summary>A torpedo or bolt: a bright star that grows as it comes closer.</summary>
    private void DrawViewedGlow(DrawingContext context, Point position, double height, double size, Color color, bool spiky)
    {
        var offset = World.Delta(_engine.Ship.Position, position);
        var v = ToCamera(offset, height);
        if (v.Forward < NearPlane || offset.Length > ViewRange) return;

        var p = v.Screen;
        double r = Math.Clamp(size * Focal / v.Forward * 0.25, 2, 40);
        context.DrawEllipse(GetBrush(color, 0.25), null, p, r * 1.8, r * 1.8);
        context.DrawEllipse(GetBrush(color, 0.9), null, p, r * 0.6, r * 0.6);
        if (spiky)
        {
            double a = Clock * 9;
            for (int i = 0; i < 4; i++)
            {
                double angle = a + i * Math.PI / 4;
                var spoke = new Vector(Math.Cos(angle), Math.Sin(angle)) * r * 1.6;
                DrawGlowLine(context, p - spoke, p + spoke, color, 0.9, 1.2);
            }
        }
    }

    private void DrawViewedExplosion(DrawingContext context, Explosion explosion)
    {
        var offset = World.Delta(_engine.Ship.Position, explosion.Position);
        var v = ToCamera(offset, explosion.Altitude);
        if (v.Forward < NearPlane || offset.Length > ViewRange) return;

        var centre = v.Screen;
        double k = Focal / v.Forward;
        double alpha = explosion.GetOpacity() * DistanceFade(offset.Length);
        foreach (var (a, b) in explosion.GetParticleLines())
        {
            var pa = centre + (a - explosion.Position) * k;
            var pb = centre + (b - explosion.Position) * k;
            DrawGlowLine(context, pa, pb, explosion.Color, alpha, Math.Clamp(k * 1.5, 1, 3));
        }
    }

    // Phaser fire: two beams from the lower corners of the view, meeting where the shot lands.
    private void DrawViewerPhasers(DrawingContext context)
    {
        foreach (var beam in _engine.Phasers)
        {
            double alpha = 1 - beam.Age / PhaserBeam.ShowTime;
            var target = new Point(ViewerCentre.X, ViewerCentre.Y + Math.Min(40, 900 / Math.Max(beam.Length, 20)));
            var left = new Point(ViewerRect.X + 30, ViewerRect.Bottom);
            var right = new Point(ViewerRect.Right - 30, ViewerRect.Bottom);
            DrawGlowLine(context, left, target, PhaserColor, alpha, 3);
            DrawGlowLine(context, right, target, PhaserColor, alpha, 3);
            if (beam.Hit)
                context.DrawEllipse(GetBrush(Colors.White, alpha * 0.8), null, target, 6, 6);
        }
    }

    private void DrawCrosshair(DrawingContext context)
    {
        if (!_engine.ShipAlive) return;
        var c = ViewerCentre;
        var color = Color.FromRgb(255, 210, 90);
        DrawGlowLine(context, c + new Vector(-26, 0), c + new Vector(-10, 0), color, 0.75, 1.2);
        DrawGlowLine(context, c + new Vector(10, 0), c + new Vector(26, 0), color, 0.75, 1.2);
        DrawGlowLine(context, c + new Vector(0, -20), c + new Vector(0, -8), color, 0.75, 1.2);
        DrawGlowLine(context, c + new Vector(0, 8), c + new Vector(0, 20), color, 0.75, 1.2);
    }

    private void DrawViewerOverlays(DrawingContext context)
    {
        var centre = ViewerCentre;

        if (_engine.HitFlash > 0)
            context.FillRectangle(GetBrush(AlertRed, Math.Min(0.35, _engine.HitFlash)), ViewerRect);

        if (_engine.ShipAlive && _engine.Ship.IsWarping)
            DrawCentredText(context, "WARP", 16, ViewerRect.Bottom - 26, Color.FromRgb(170, 200, 255), centre.X);

        if (_engine.IsDocking)
        {
            var bar = new Rect(centre.X - 120, ViewerRect.Bottom - 34, 240, 10);
            DrawFrame(context, bar, GameEngine.StarbaseColor, 0.8);
            context.FillRectangle(GetBrush(GameEngine.StarbaseColor, 0.8),
                new Rect(bar.X, bar.Y, bar.Width * _engine.DockingProgress, bar.Height));
        }

        if (_engine.State == GameState.Attract)
            DrawCentredText(context, "DEMONSTRATION", 18, ViewerRect.Y + 26, Gold, centre.X);

        if (_engine.Message != null && _engine.MessageTime > 0 && _engine.State != GameState.GameOver)
        {
            double alpha = Math.Min(1, _engine.MessageTime * 2);
            DrawCentredText(context, _engine.Message, 30, ViewerRect.Y + 70, Gold, centre.X, alpha);
            if (_engine.SubMessage != null)
                DrawCentredText(context, _engine.SubMessage, 18, ViewerRect.Y + 112, TextColor, centre.X, alpha);
        }
        else if (!_engine.ShipAlive && _engine.State != GameState.GameOver && _engine.Lives > 0)
        {
            DrawCentredText(context, "ENTERPRISE DESTROYED", 26, ViewerRect.Y + 70, AlertRed, centre.X);
        }
    }
}

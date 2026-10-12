using System;
using System.Linq;
using Avalonia;
using AVATrek.Models;

namespace AVATrek.Engine;

/// <summary>What the ship is told to do this frame, by the player's input or by the attract-mode pilot.</summary>
public readonly record struct ShipControls(bool TurnLeft, bool TurnRight, bool Impulse, bool Phaser, bool Torpedo, bool Warp);

/// <summary>
/// Flies the Enterprise in attract mode. It turns on the nearest threat and works it over with
/// phasers, saves torpedoes for Klingons and Nomad, warps to shake off saucers, and heads for a
/// starbase when the shields run low. Competent, not perfect: it loses ships.
/// </summary>
public class AttractPilot
{
    private static readonly Random Random = new();

    private bool _fireToggle;
    private double _warpBurst;
    private double _hesitation;

    public void Reset()
    {
        _warpBurst = 0;
        _hesitation = 0;
    }

    public ShipControls Fly(GameEngine engine, double deltaTime)
    {
        if (!engine.ShipAlive || engine.IsDocking)
            return default;

        var ship = engine.Ship;
        _fireToggle = !_fireToggle;

        // Now and then it looks away from the board for a moment.
        _hesitation -= deltaTime;
        if (_hesitation < -5 && Random.NextDouble() < deltaTime * 0.2)
            _hesitation = 0.5 + Random.NextDouble() * 0.7;
        bool alert = _hesitation <= 0;

        // Saucers on the hull: a short burst of warp throws them off.
        _warpBurst -= deltaTime;
        if (alert && engine.Saucers.Any(s => s.IsAttached) && ship.WarpEnergy > 20 && _warpBurst < -1)
            _warpBurst = 0.35;
        bool warp = _warpBurst > 0;

        // Low on shields: make for the nearest starbase.
        Starbase? haven = ship.Shields < 35
            ? engine.Starbases.OrderBy(b => World.Distance(b.Position, ship.Position)).FirstOrDefault()
            : null;
        if (haven != null)
        {
            var d = World.Delta(ship.Position, haven.Position);
            double bearing = Math.Atan2(d.Y, d.X);
            double diff = World.AngleDiff(bearing, ship.Rotation);
            // Far off it closes at speed; near the base it lets the drag bleed speed off and then
            // nudges in slowly, so it doesn't end up circling the base at full impulse.
            bool aligned = Math.Abs(diff) < (d.Length > 150 ? 0.25 : 0.12);
            double allowed = d.Length > 150 ? 200 : 40 + d.Length * 0.4;
            return new ShipControls(diff < -0.05, diff > 0.05, aligned && ship.Velocity.Length < allowed, false, false, warp);
        }

        GameObject? target = ChooseTarget(engine, ship);
        if (target == null)
        {
            // Nothing about: idle toward the middle of the sector.
            var d = World.Delta(ship.Position, World.Center);
            double diff = World.AngleDiff(Math.Atan2(d.Y, d.X), ship.Rotation);
            return new ShipControls(diff < -0.1, diff > 0.1, d.Length > 150 && Math.Abs(diff) < 0.3, false, false, warp);
        }

        var toTarget = World.Delta(ship.Position, target.Position);
        double distance = toTarget.Length;

        // Lead a moving target a little.
        double lead = distance / 900;
        var aimAt = toTarget + target.Velocity * lead;
        double aim = World.AngleDiff(Math.Atan2(aimAt.Y, aimAt.X), ship.Rotation);
        bool onTarget = Math.Abs(aim) < Math.Max(0.06, Math.Atan2(target.Radius * 0.8, distance));

        bool phaser = alert && onTarget && distance < Enterprise.PhaserRange - 10 && _fireToggle;
        bool big = target is Klingon or Nomad;
        bool torpedo = alert && big && onTarget && distance < 320 && ship.Photons > 1 &&
                       Random.NextDouble() < deltaTime * 1.5;
        bool impulse = Math.Abs(aim) < 0.3 && distance > 220;

        return new ShipControls(aim < -0.04, aim > 0.04, impulse, phaser, torpedo, warp);
    }

    private static GameObject? ChooseTarget(GameEngine engine, Enterprise ship)
    {
        double Dist(GameObject o) => World.Distance(o.Position, ship.Position);

        var close = engine.Saucers.Where(s => !s.IsAttached && Dist(s) < 200)
            .Concat<Enemy>(engine.Mines.Where(m => Dist(m) < 110))
            .OrderBy(Dist)
            .FirstOrDefault();
        if (close != null) return close;

        return engine.Klingons.Where(k => k.HasArrived).Cast<Enemy>()
            .Concat(engine.Nomad != null ? new Enemy[] { engine.Nomad } : Array.Empty<Enemy>())
            .Concat(engine.Saucers.Where(s => !s.IsAttached))
            .OrderBy(Dist)
            .FirstOrDefault();
    }
}

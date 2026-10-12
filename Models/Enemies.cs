using System;
using Avalonia;

namespace AVATrek.Models;

/// <summary>Anything the player can shoot: it has hit points and is worth points.</summary>
public abstract class Enemy : GameObject, IModelled
{
    protected static readonly Random Random = new();

    public int Health { get; set; }
    public int Points { get; }

    /// <summary>A short flash after a hit that didn't finish it off.</summary>
    public double HitFlash { get; set; }

    public abstract Wireframe Model { get; }
    public abstract double ModelSize { get; }
    public virtual double ModelYaw => Rotation;

    protected Enemy(Point position, double radius, int health, int points) : base(position, radius)
    {
        Health = health;
        Points = points;
    }

    public override void Update(double deltaTime)
    {
        HitFlash = Math.Max(0, HitFlash - deltaTime);
        base.Update(deltaTime);
    }
}

/// <summary>
/// A Klingon battle cruiser. It picks a target (usually the Enterprise, sometimes a starbase), swings
/// round it at a stand-off distance, and fires a disruptor bolt whenever its nose comes round onto it.
/// </summary>
public class Klingon : Enemy
{
    public const int ScoreValue = 500;

    private readonly double _speed;
    private readonly double _turnRate;
    private readonly double _fireInterval;
    private readonly double _standOff;
    private double _orbitSide;
    private double _fireTimer;
    private double _bob;

    /// <summary>Seconds left in the shimmer of arriving from warp; it can't fire or be hit until it has.</summary>
    public double ArrivalTime { get; private set; } = 0.8;
    public bool HasArrived => ArrivalTime <= 0;

    /// <summary>The starbase it has gone after, or null for the Enterprise.</summary>
    public Starbase? TargetBase { get; set; }

    public double DisruptorSpeed { get; }

    public override Wireframe Model => Wireframe.Klingon;
    public override double ModelSize => 19;

    public Klingon(Point position, double rotation, int sector) : base(position, 15, 3, ScoreValue)
    {
        int k = Math.Min(sector - 1, 9);
        Rotation = rotation;
        _speed = 120 + 9 * k;
        _turnRate = 1.6 + 0.12 * k;
        _fireInterval = Math.Max(1.0, 2.8 - 0.18 * k);
        DisruptorSpeed = 280 + 15 * k;
        _standOff = 150 + Random.NextDouble() * 110;
        _orbitSide = Random.Next(2) == 0 ? 1 : -1;
        _fireTimer = 1.0 + Random.NextDouble() * _fireInterval;
        _bob = Random.NextDouble() * Math.PI * 2;
        Velocity = Heading * _speed;
    }

    /// <summary>Steers and returns true when it wants to fire at the target this frame.</summary>
    public bool Think(Point target, double deltaTime)
    {
        _bob += deltaTime;
        Altitude = Math.Sin(_bob * 0.7) * 18;

        if (!HasArrived)
        {
            ArrivalTime -= deltaTime;
            Update(deltaTime);
            return false;
        }

        // Aim for a point off to one side of the target, so it circles rather than rams.
        var toTarget = World.Delta(Position, target);
        double distance = toTarget.Length;
        double bearing = Math.Atan2(toTarget.Y, toTarget.X);
        double wanted = distance > _standOff
            ? bearing + _orbitSide * Math.Clamp(_standOff / Math.Max(distance, 1), 0, 1) * 0.9
            : bearing + _orbitSide * Math.PI / 2;

        _fireTimer -= deltaTime;
        bool lined = Math.Abs(World.AngleDiff(bearing, Rotation)) < 0.45 && distance < 420;

        // When the gun comes due it turns in to take the shot.
        if (_fireTimer <= 0 && distance < 420)
            wanted = bearing;

        Rotation = World.TurnToward(Rotation, wanted, _turnRate * deltaTime);
        Velocity = Velocity * 0.9 + Heading * _speed * 0.1;

        if (Random.NextDouble() < deltaTime * 0.1)
            _orbitSide = -_orbitSide;

        Update(deltaTime);

        if (_fireTimer <= 0 && lined)
        {
            _fireTimer = _fireInterval * (0.75 + Random.NextDouble() * 0.5);
            return true;
        }
        return false;
    }
}

/// <summary>
/// An anti-matter saucer. Small, quick and fragile, it dives at the Enterprise and clamps on to the
/// hull, where it drains the shields. Phasers can't reach one that has latched on: warp shakes it off
/// (and destroys it), docking clears it, or it lets go on its own after a while and comes round again.
/// </summary>
public class Saucer : Enemy
{
    public const int ScoreValue = 200;
    public const double DrainRate = 9;       // shield points per second, each
    private const double HoldTime = 7;

    private readonly double _speed;
    private double _wobble;
    private double _spin;
    private double _attachedFor;
    private double _cooldown;

    public bool IsAttached { get; private set; }

    /// <summary>Where on the hull it sits, as an angle relative to the ship's heading.</summary>
    public double AttachAngle { get; private set; }

    public override Wireframe Model => Wireframe.Saucer;
    public override double ModelSize => 11;
    public override double ModelYaw => _spin;

    public Saucer(Point position, int sector) : base(position, 9, 1, ScoreValue)
    {
        int k = Math.Min(sector - 1, 9);
        _speed = 175 + 11 * k;
        _wobble = Random.NextDouble() * 10;
        Altitude = (Random.NextDouble() - 0.5) * 30;
    }

    public void Attach(Enterprise ship)
    {
        IsAttached = true;
        _attachedFor = 0;
        AttachAngle = World.AngleTo(ship.Position, Position) - ship.Rotation;
    }

    public void Release(Enterprise ship)
    {
        IsAttached = false;
        _cooldown = 2.5;
        double away = ship.Rotation + AttachAngle;
        Velocity = new Vector(Math.Cos(away), Math.Sin(away)) * _speed;
    }

    public bool CanAttach => !IsAttached && _cooldown <= 0;

    /// <summary>Flies one frame; returns true if it let go of the ship this frame.</summary>
    public bool Think(Enterprise ship, bool shipAlive, double deltaTime)
    {
        _spin += deltaTime * 6;
        _wobble += deltaTime;
        HitFlash = Math.Max(0, HitFlash - deltaTime);

        if (IsAttached)
        {
            _attachedFor += deltaTime;
            double a = ship.Rotation + AttachAngle;
            Position = World.Wrap(ship.Position + new Vector(Math.Cos(a), Math.Sin(a)) * (ship.Radius + 4));
            Velocity = ship.Velocity;
            Altitude = 0;
            if (_attachedFor >= HoldTime || !shipAlive)
            {
                Release(ship);
                return true;
            }
            return false;
        }

        _cooldown = Math.Max(0, _cooldown - deltaTime);
        if (shipAlive && _cooldown <= 0)
        {
            // Dive at the ship, weaving.
            double bearing = World.AngleTo(Position, ship.Position) + Math.Sin(_wobble * 3.1) * 0.6;
            var wanted = new Vector(Math.Cos(bearing), Math.Sin(bearing)) * _speed;
            Velocity += (wanted - Velocity) * Math.Min(1, deltaTime * 2.5);
        }
        else if (Velocity.Length < 1)
        {
            Velocity = new Vector(Math.Cos(_wobble), Math.Sin(_wobble)) * _speed * 0.5;
        }

        Altitude = Math.Sin(_wobble * 1.7) * 14;
        base.Update(deltaTime);
        return false;
    }
}

/// <summary>
/// Nomad, the rogue probe: big, slow and tough. It wanders the sector from waypoint to waypoint and
/// sows mines behind it as it goes.
/// </summary>
public class Nomad : Enemy
{
    public const int ScoreValue = 3000;

    private Point _waypoint;
    private double _mineTimer;
    private readonly double _mineInterval;
    private double _spin;
    private double _retarget;

    public int MaxHealth { get; }

    public override Wireframe Model => Wireframe.Nomad;
    public override double ModelSize => 24;
    public override double ModelYaw => _spin;

    public Nomad(Point position, int sector) : base(position, 22, Math.Min(80, 40 + 5 * (sector - 1)), ScoreValue)
    {
        MaxHealth = Health;
        _mineInterval = Math.Max(0.9, 1.7 - 0.08 * (sector - 1));
        _mineTimer = 2;
        _waypoint = RandomPoint();
    }

    private static Point RandomPoint() =>
        new(60 + Random.NextDouble() * (World.Width - 120), 50 + Random.NextDouble() * (World.Height - 100));

    /// <summary>Moves one frame; returns true when it lays a mine.</summary>
    public bool Think(double deltaTime)
    {
        _spin += deltaTime * 0.8;
        Altitude = Math.Sin(_spin * 1.3) * 10;

        // It changes its mind about where it's going every few seconds, and lurches as it turns.
        var toWaypoint = World.Delta(Position, _waypoint);
        _retarget -= deltaTime;
        if (toWaypoint.Length < 30 || _retarget <= 0)
        {
            _waypoint = RandomPoint();
            _retarget = 2.5 + Random.NextDouble() * 2.5;
        }

        Rotation = World.TurnToward(Rotation, Math.Atan2(toWaypoint.Y, toWaypoint.X), 1.6 * deltaTime);
        Velocity = Heading * (100 + 40 * Math.Sin(_spin * 2.3));
        Update(deltaTime);

        _mineTimer -= deltaTime;
        if (_mineTimer <= 0)
        {
            _mineTimer = _mineInterval;
            return true;
        }
        return false;
    }

    /// <summary>Where a new mine is dropped: just behind the probe.</summary>
    public Point Tail => World.Wrap(Position - Heading * (Radius + 10));
}

/// <summary>A mine sown by Nomad. It drifts a little, and blows up on whatever touches it.</summary>
public class Mine : Enemy
{
    public const int ScoreValue = 100;
    public const double Lifetime = 30;

    private double _spin;

    public double Age { get; private set; }
    public bool IsFading => Lifetime - Age < 3;

    public override Wireframe Model => Wireframe.Mine;
    public override double ModelSize => 9;
    public override double ModelYaw => _spin;

    public Mine(Point position) : base(position, 8, 1, ScoreValue)
    {
        double a = Random.NextDouble() * Math.PI * 2;
        Velocity = new Vector(Math.Cos(a), Math.Sin(a)) * (6 + Random.NextDouble() * 10);
        Altitude = (Random.NextDouble() - 0.5) * 20;
        _spin = a;
    }

    public override void Update(double deltaTime)
    {
        _spin += deltaTime * 1.5;
        Age += deltaTime;
        if (Age >= Lifetime) IsAlive = false;
        base.Update(deltaTime);
    }
}

using System;
using Avalonia;

namespace AVATrek.Models;

/// <summary>
/// A Federation starbase. Fly into its centre to dock: shields, torpedoes and warp energy are all
/// refilled, and the base is used up. Klingons shoot at them, and enough hits destroy one. A base
/// still standing unused at the end of a sector is worth a bonus.
/// </summary>
public class Starbase : GameObject, IModelled
{
    public const int MaxHealth = 6;
    public const double DockRadius = 20;
    public const double DockTime = 1.2;

    private double _spin;

    public int Health { get; set; } = MaxHealth;
    public double HitFlash { get; set; }

    public Wireframe Model => Wireframe.Starbase;
    public double ModelSize => 30;
    public double ModelYaw => _spin;

    public Starbase(Point position) : base(position, 26)
    {
        Altitude = -8;
        _spin = position.X;
    }

    public override void Update(double deltaTime)
    {
        _spin += deltaTime * 0.35;
        HitFlash = Math.Max(0, HitFlash - deltaTime);
    }
}

/// <summary>A photon torpedo: it flies straight, and bursts on the first enemy it meets or at the end of its run.</summary>
public class Torpedo : GameObject
{
    public const double Speed = 440;
    public const double Life = 1.4;
    public const double BlastRadius = 70;
    public const int Damage = 4;

    public double Age { get; private set; }

    public Torpedo(Point position, double rotation, Vector shipVelocity) : base(position, 5)
    {
        Rotation = rotation;
        // Half the ship's own speed is carried along, so a torpedo always leaves the ship ahead of it.
        Velocity = Heading * Speed + shipVelocity * 0.5;
    }

    public override void Update(double deltaTime)
    {
        Age += deltaTime;
        if (Age >= Life) IsAlive = false;
        base.Update(deltaTime);
    }
}

/// <summary>A Klingon disruptor bolt.</summary>
public class Disruptor : GameObject
{
    public const double Life = 1.8;

    public double Age { get; private set; }
    public Point PreviousPosition { get; private set; }

    public Disruptor(Point position, double rotation, double speed) : base(position, 4)
    {
        Rotation = rotation;
        Velocity = Heading * speed;
        PreviousPosition = position;
    }

    public override void Update(double deltaTime)
    {
        PreviousPosition = Position;
        Age += deltaTime;
        if (Age >= Life) IsAlive = false;
        base.Update(deltaTime);
    }
}

/// <summary>A phaser shot: an instant beam that shows for a moment.</summary>
public class PhaserBeam
{
    public const double ShowTime = 0.14;

    public Point Start { get; }
    public double Rotation { get; }
    public double Length { get; }
    public double Age { get; private set; }
    public bool Hit { get; }
    public bool IsFinished => Age >= ShowTime;

    public PhaserBeam(Point start, double rotation, double length, bool hit)
    {
        Start = start;
        Rotation = rotation;
        Length = length;
        Hit = hit;
    }

    public Point End => Start + new Vector(Math.Cos(Rotation), Math.Sin(Rotation)) * Length;

    public void Update(double deltaTime) => Age += deltaTime;
}

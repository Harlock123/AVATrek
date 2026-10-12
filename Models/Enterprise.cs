using System;
using Avalonia;

namespace AVATrek.Models;

/// <summary>
/// The player's starship. It turns, runs on impulse engines with a little drag (so it coasts to a
/// stop rather than drifting for ever), and can jump to warp while warp energy lasts. Shields,
/// photon torpedoes and warp energy are all spent in battle and only refilled by docking at a starbase.
/// </summary>
public class Enterprise : GameObject, IModelled
{
    public const double MaxShields = 100;
    public const double MaxWarp = 100;
    public const int MaxPhotons = 5;

    public const double TurnSpeed = 3.3;         // radians per second
    public const double ImpulseAccel = 300;
    public const double ImpulseMax = 240;
    public const double Drag = 0.55;             // fraction of speed lost per second when coasting
    public const double WarpSpeed = 760;
    public const double WarpDrain = 30;          // warp energy per second
    public const double WarpMinimum = 5;         // needed to engage
    public const double PhaserRange = 280;
    public const double PhaserCooldown = 0.28;
    public const double TorpedoCooldown = 0.45;
    public const double SpawnShield = 2.5;       // seconds of immunity after arriving

    public double Shields { get; set; } = MaxShields;
    public double WarpEnergy { get; set; } = MaxWarp;
    public int Photons { get; set; } = MaxPhotons;

    public bool IsWarping { get; private set; }
    public double InvulnerableTime { get; set; }
    public bool IsInvulnerable => InvulnerableTime > 0 || IsWarping;
    public double PhaserTimer { get; set; }
    public double TorpedoTimer { get; set; }

    public Wireframe Model => Wireframe.Enterprise;
    public double ModelSize => 17;
    public double ModelYaw => Rotation;

    public Enterprise(Point position, double rotation) : base(position, 13)
    {
        Rotation = rotation;
    }

    public void Respawn(Point position, double rotation)
    {
        Position = position;
        Rotation = rotation;
        Velocity = default;
        Shields = MaxShields;
        WarpEnergy = MaxWarp;
        Photons = MaxPhotons;
        IsWarping = false;
        InvulnerableTime = SpawnShield;
        PhaserTimer = TorpedoTimer = 0;
        IsAlive = true;
    }

    public void Turn(double direction, double deltaTime) => Rotation += direction * TurnSpeed * deltaTime;

    /// <summary>Moves the ship one frame. Returns true if the warp engines are running.</summary>
    public bool Fly(bool impulse, bool warp, double deltaTime)
    {
        if (warp && WarpEnergy >= (IsWarping ? 0.01 : WarpMinimum))
        {
            IsWarping = true;
            WarpEnergy = Math.Max(0, WarpEnergy - WarpDrain * deltaTime);
            Velocity = Heading * WarpSpeed;
        }
        else
        {
            if (IsWarping)
            {
                // Dropping out of warp leaves the ship at full impulse.
                IsWarping = false;
                Velocity = Heading * ImpulseMax;
            }

            if (impulse)
            {
                Velocity += Heading * ImpulseAccel * deltaTime;
                if (Velocity.Length > ImpulseMax)
                    Velocity = Velocity / Velocity.Length * ImpulseMax;
            }
            else
            {
                Velocity *= Math.Max(0, 1 - Drag * deltaTime);
            }
        }

        InvulnerableTime = Math.Max(0, InvulnerableTime - deltaTime);
        PhaserTimer = Math.Max(0, PhaserTimer - deltaTime);
        TorpedoTimer = Math.Max(0, TorpedoTimer - deltaTime);
        Update(deltaTime);
        return IsWarping;
    }

    public void DropOutOfWarp()
    {
        if (IsWarping)
        {
            IsWarping = false;
            Velocity = default;
        }
    }

    public Point Nose => World.Wrap(Position + Heading * 16);
}

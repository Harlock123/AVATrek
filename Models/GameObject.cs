using System;
using Avalonia;

namespace AVATrek.Models;

public abstract class GameObject
{
    public Point Position { get; set; }
    public Vector Velocity { get; set; }
    public double Rotation { get; set; }
    public double Radius { get; protected set; }
    public bool IsAlive { get; set; } = true;

    /// <summary>Height above or below the sector's plane. Only the viewer shows it; the game is flat.</summary>
    public double Altitude { get; protected set; }

    protected GameObject(Point position, double radius)
    {
        Position = position;
        Radius = radius;
    }

    public Vector Heading => new(Math.Cos(Rotation), Math.Sin(Rotation));

    public virtual void Update(double deltaTime)
    {
        Position = World.Wrap(Position + Velocity * deltaTime);
    }

    public bool CollidesWith(GameObject other) =>
        World.Distance(Position, other.Position) < Radius + other.Radius;
}

/// <summary>Something drawn as a wireframe model, at a size and with an optional spin about its own axis.</summary>
public interface IModelled
{
    Wireframe Model { get; }
    double ModelSize { get; }

    /// <summary>The direction the model points: the object's rotation, or its spin for round things.</summary>
    double ModelYaw { get; }
}

using System.Numerics;
using Animata.Core.Bodies;
using Animata.Core.Entities;
using Animata.Core.Physics;
using BepuPhysics;
using BepuPhysics.Collidables;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Nieruchomy słupek-przeszkoda (pionowy walec stojący na Body.Position). Kolizje go nie przesuwają, wąsy go widzą.
/// W świecie z fizyką jest statycznym walcem; przesunięty albo zmieniony ręcznie — odtwarza się przed następnym krokiem.
/// </summary>
public sealed class Obstacle : StaticEntity, IPhysicalEntity
{
    private StaticHandle? _static;
    private (Vector3 Position, float Radius, float Height) _built;

    public Obstacle() : base(new Body())
    {
    }

    public float Radius { get; set; } = 0.5f;
    public float Height { get; set; } = 0.8f;

    public override float BoundingRadius => Radius * MathF.Max(Body.Scale.X, Body.Scale.Y);

    public override EntityCategory Category => EntityCategory.Obstacle;

    bool IPhysicalEntity.IsDynamic => false;

    void IPhysicalEntity.AttachPhysics(PhysicsWorld aPhysics)
    {
        var radius = MathF.Max(0.01f, BoundingRadius);
        var height = MathF.Max(0.01f, Height * Body.Scale.Z);
        // Walec Bepu stoi wzdłuż lokalnej osi Y — obrót o 90° wokół X stawia go pionowo (wzdłuż Z).
        var pose = new RigidPose(Body.Position + new Vector3(0, 0, height / 2),
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2));
        _static = aPhysics.AddStatic(new Cylinder(radius, height), pose);
        _built = (Body.Position, Radius, Height);
    }

    void IPhysicalEntity.DetachPhysics(PhysicsWorld aPhysics)
    {
        if (_static is { } handle)
            aPhysics.RemoveStatic(handle);
        _static = null;
    }

    void IPhysicalEntity.BeforePhysicsStep(PhysicsWorld aPhysics, float aDelta)
    {
        if (_built == (Body.Position, Radius, Height))
            return;
        ((IPhysicalEntity)this).DetachPhysics(aPhysics);
        ((IPhysicalEntity)this).AttachPhysics(aPhysics);
    }

    void IPhysicalEntity.AfterPhysicsStep(PhysicsWorld aPhysics)
    {
    }
}

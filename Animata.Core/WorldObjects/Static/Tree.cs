using System.Numerics;
using Animata.Core.Bodies;
using Animata.Core.Entities;
using Animata.Core.Physics;
using BepuPhysics;
using BepuPhysics.Collidables;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Drzewo — pionowy pień (walec stojący na Body.Position) do wspinania. Kora ma tarcie chwytne (<see cref="Bark"/>):
/// kontakt z pniem ma takie tarcie niezależnie od tarcia ciała, więc wąż owinięty i ściśnięty wokół pnia się trzyma,
/// a toczony zwój (skręt i pochylenie stawów w obracającej się płaszczyźnie) wkręca się w górę jak śruba.
/// Wąsy widzą je jak słupek (kategoria <see cref="EntityCategory.Obstacle"/>).
/// </summary>
public sealed class Tree : StaticEntity, IPhysicalEntity
{
    private StaticHandle? _static;
    private (Vector3 Position, float Radius, float Height, float Bark) _built;

    public Tree() : base(new Body())
    {
    }

    public float Radius { get; set; } = 0.25f;
    public float Height { get; set; } = 4;

    /// <summary>Tarcie kory.</summary>
    public float Bark { get; set; } = 1.2f;

    public override float BoundingRadius => Radius;

    public override EntityCategory Category => EntityCategory.Obstacle;

    bool IPhysicalEntity.IsDynamic => false;

    void IPhysicalEntity.AttachPhysics(PhysicsWorld aPhysics)
    {
        var radius = MathF.Max(0.02f, Radius);
        var height = MathF.Max(0.1f, Height);
        // Walec Bepu stoi wzdłuż lokalnej osi Y — obrót o 90° wokół X stawia go pionowo.
        var pose = new RigidPose(Body.Position + new Vector3(0, 0, height / 2), Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2));
        _static = aPhysics.AddStatic(new Cylinder(radius, height), pose, Bark);
        _built = (Body.Position, Radius, Height, Bark);
    }

    void IPhysicalEntity.DetachPhysics(PhysicsWorld aPhysics)
    {
        if (_static is { } handle)
            aPhysics.RemoveStatic(handle);
        _static = null;
    }

    void IPhysicalEntity.BeforePhysicsStep(PhysicsWorld aPhysics, float aDelta)
    {
        if (_built == (Body.Position, Radius, Height, Bark))
            return;
        ((IPhysicalEntity)this).DetachPhysics(aPhysics);
        ((IPhysicalEntity)this).AttachPhysics(aPhysics);
    }

    void IPhysicalEntity.AfterPhysicsStep(PhysicsWorld aPhysics)
    {
    }
}

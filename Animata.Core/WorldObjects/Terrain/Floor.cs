using System.Numerics;
using Animata.Core.Bodies;
using Animata.Core.Entities;
using Animata.Core.Physics;
using BepuPhysics;
using BepuPhysics.Collidables;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Podłoga: prostopadłościan wyrównany do osi (AABB), środek w <see cref="Body"/>.Position, wymiary <see cref="Size"/>.
/// Całkowicie nieruszalna (<see cref="Entity.IsFixed"/>): nie przesuwa jej mysz, panel ani fizyka.
/// W fizyce (Bepu) jest statycznym pudłem, po którym chodzą ciała; wąsy jej nie widzą (kategoria <see cref="EntityCategory.Ground"/>).
/// </summary>
public sealed class Floor : PhysicalStaticEntity
{
    public Floor(Vector3 aCenter, Vector3 aSize) : base(new Body { Position = aCenter })
    {
        if (!(aSize.X > 0 && aSize.Y > 0 && aSize.Z > 0))
            throw new ArgumentOutOfRangeException(nameof(aSize), aSize, "Floor size must be positive.");
        Size = aSize;
    }

    /// <summary>Pełne wymiary pudła (X × Y × Z).</summary>
    public Vector3 Size { get; }

    /// <summary>Wysokość górnej ściany — tu stoją stwory.</summary>
    public float Top => Body.Position.Z + Size.Z / 2;

    public Vector3 Min => Body.Position - Size / 2;
    public Vector3 Max => Body.Position + Size / 2;

    public override bool IsFixed => true;

    public override EntityCategory Category => EntityCategory.Ground;

    /// <summary>Podłoga o górnej ścianie na z = 0.</summary>
    public static Floor At(float aWidth, float aDepth, float aThickness = 0.2f, float aCenterX = 0, float aCenterY = 0) =>
        new(new Vector3(aCenterX, aCenterY, -aThickness / 2), new Vector3(aWidth, aDepth, aThickness));

    /// <summary>Podłoga się nie zmienia (<see cref="IsFixed"/>).</summary>
    protected override object Shape => Size;

    protected override StaticHandle Build(PhysicsWorld aPhysics) =>
        aPhysics.AddStatic(new Box(Size.X, Size.Y, Size.Z), new RigidPose(Body.Position));
}

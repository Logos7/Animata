using System.Numerics;
using Animata.Core.Bodies;
using Animata.Core.Entities;
using Animata.Core.Physics;
using BepuPhysics;
using BepuPhysics.Collidables;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Płyta — płaska przeszkoda terenu: prostopadłościan leżący na Body.Position (środek spodu), wymiary <see cref="Size"/>
/// (szerokość X × głębokość Y × wysokość Z w układzie płyty), obrót wokół pionu z Body.Rotation.
/// Stwory w fizyce wchodzą na nią, obchodzą ją albo się o nią zatrzymują. Wąsy jej nie widzą (kategoria
/// <see cref="EntityCategory.Ground"/> — to teren, nie słupek), kolizje okręgów też nie (promień 0).
/// Da się ją przesuwać i zmieniać; w fizyce odtwarza się przed następnym krokiem.
/// </summary>
public sealed class Slab : StaticEntity, IPhysicalEntity
{
    public const float MinSide = 0.1f;
    public const float MaxSide = 20;
    public const float MinHeight = 0.01f;
    public const float MaxHeight = 2;

    private StaticHandle? _static;
    private (Vector3 Position, Quaternion Rotation, Vector3 Size) _built;
    private Vector3 _size = new(1.5f, 1, 0.06f);

    public Slab() : base(new Body())
    {
    }

    public Vector3 Size
    {
        get => _size;
        set
        {
            if (!(value.X >= MinSide && value.X <= MaxSide && value.Y >= MinSide && value.Y <= MaxSide &&
                  value.Z >= MinHeight && value.Z <= MaxHeight))
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    $"Płyta ma boki {MinSide}–{MaxSide} m i wysokość {MinHeight}–{MaxHeight} m.");
            _size = value;
        }
    }

    /// <summary>Wysokość górnej ściany.</summary>
    public float Top => Body.Position.Z + Size.Z;

    public override EntityCategory Category => EntityCategory.Ground;

    /// <summary>Czy punkt (x, y) leży nad płytą (z uwzględnieniem obrotu).</summary>
    public bool Covers(Vector2 aPoint, float aMargin = 0)
    {
        var local = Vector3.Transform(new Vector3(aPoint - new Vector2(Body.Position.X, Body.Position.Y), 0),
            Quaternion.Inverse(Body.Rotation));
        return MathF.Abs(local.X) <= Size.X / 2 + aMargin && MathF.Abs(local.Y) <= Size.Y / 2 + aMargin;
    }

    bool IPhysicalEntity.IsDynamic => false;

    void IPhysicalEntity.AttachPhysics(PhysicsWorld aPhysics)
    {
        var center = Body.Position + Vector3.Transform(new Vector3(0, 0, Size.Z / 2), Body.Rotation);
        _static = aPhysics.AddStatic(new Box(Size.X, Size.Y, Size.Z), new RigidPose(center, Body.Rotation));
        _built = (Body.Position, Body.Rotation, Size);
    }

    void IPhysicalEntity.DetachPhysics(PhysicsWorld aPhysics)
    {
        if (_static is { } handle)
            aPhysics.RemoveStatic(handle);
        _static = null;
    }

    void IPhysicalEntity.BeforePhysicsStep(PhysicsWorld aPhysics, float aDelta)
    {
        if (_built == (Body.Position, Body.Rotation, Size))
            return;
        ((IPhysicalEntity)this).DetachPhysics(aPhysics);
        ((IPhysicalEntity)this).AttachPhysics(aPhysics);
    }

    void IPhysicalEntity.AfterPhysicsStep(PhysicsWorld aPhysics)
    {
    }
}

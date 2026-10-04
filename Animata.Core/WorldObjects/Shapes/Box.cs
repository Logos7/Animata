using System.Numerics;
using Animata.Core.Bodies;
using Animata.Core.Entities;
using Animata.Core.Physics;
using BepuPhysics;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Klocek: prostopadłościan leżący na Body.Position (środek spodu), wymiary <see cref="Size"/> (X × Y × wysokość Z
/// w układzie klocka), obrót wokół pionu z Body.Rotation. W fizyce statyczne pudło — stwory na nie wchodzą, obchodzą je
/// albo się o nie zatrzymują; przesunięty albo zmieniony odtwarza się przed następnym krokiem. Wąsy go nie widzą
/// (kategoria <see cref="EntityCategory.Ground"/> — to teren). Podłoga to zwykły klocek, tylko zablokowany (<see cref="Entity.Locked"/>).
/// </summary>
public sealed class Box() : PhysicalStaticEntity(new Body())
{
    public const float MinSide = 0.1f;
    public const float MaxSide = 100;
    public const float MinHeight = 0.01f;
    public const float MaxHeight = 5;

    public static readonly Vector3 DefaultColor = new(0.38f, 0.45f, 0.52f);

    private Vector3 _size = new(1.5f, 1, 0.06f);

    [Setting("Wymiary", Unit = "m", Tip = "Szerokość, głębokość i grubość: boki 0.1–100 m, grubość 0.01–5 m.")]
    public Vector3 Size
    {
        get => _size;
        set
        {
            if (!(value.X >= MinSide && value.X <= MaxSide && value.Y >= MinSide && value.Y <= MaxSide &&
                  value.Z >= MinHeight && value.Z <= MaxHeight))
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    $"Klocek ma boki {MinSide}–{MaxSide} m i wysokość {MinHeight}–{MaxHeight} m.");
            _size = value;
        }
    }

    [Setting("Kolor", Color = true)]
    public Vector3 Color { get; set; } = DefaultColor;

    /// <summary>Wysokość górnej ściany.</summary>
    public float Top => Body.Position.Z + Size.Z;

    public override EntityCategory Category => EntityCategory.Ground;

    /// <summary>Czy punkt (x, y) leży nad klockiem (z uwzględnieniem obrotu).</summary>
    public bool Covers(Vector2 aPoint, float aMargin = 0)
    {
        var local = Vector3.Transform(new Vector3(aPoint - new Vector2(Body.Position.X, Body.Position.Y), 0),
            Quaternion.Inverse(Body.Rotation));
        return MathF.Abs(local.X) <= Size.X / 2 + aMargin && MathF.Abs(local.Y) <= Size.Y / 2 + aMargin;
    }

    protected override StaticShapeState Shape => new(Body.Position, Body.Rotation, Size);

    protected override StaticHandle Build(PhysicsWorld aPhysics)
    {
        var center = Body.Position + Vector3.Transform(new Vector3(0, 0, Size.Z / 2), Body.Rotation);
        return aPhysics.AddStatic(new BepuPhysics.Collidables.Box(Size.X, Size.Y, Size.Z), new RigidPose(center, Body.Rotation));
    }
}

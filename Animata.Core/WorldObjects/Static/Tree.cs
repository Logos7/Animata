using Animata.Core.Bodies;
using Animata.Core.Entities;
using Animata.Core.Physics;
using BepuPhysics;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Drzewo — pionowy pień (walec stojący na Body.Position) do wspinania. Kora ma tarcie chwytne (<see cref="Bark"/>):
/// kontakt z pniem ma takie tarcie niezależnie od tarcia ciała, więc wąż owinięty i ściśnięty wokół pnia się trzyma,
/// a toczony zwój (skręt i pochylenie stawów w obracającej się płaszczyźnie) wkręca się w górę jak śruba.
/// Wąsy widzą je jak słupek (kategoria <see cref="EntityCategory.Obstacle"/>).
/// </summary>
public sealed class Tree() : PhysicalStaticEntity(new Body())
{
    public float Radius { get; set; } = 0.25f;
    public float Height { get; set; } = 4;

    /// <summary>Tarcie kory.</summary>
    public float Bark { get; set; } = 1.2f;

    public override float BoundingRadius => Radius;

    public override EntityCategory Category => EntityCategory.Obstacle;

    protected override object Shape => (Body.Position, Radius, Height, Bark);

    protected override StaticHandle Build(PhysicsWorld aPhysics) =>
        AddUprightCylinder(aPhysics, Body.Position, MathF.Max(0.02f, Radius), MathF.Max(0.1f, Height), Bark);
}

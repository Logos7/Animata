using Animata.Core.Bodies;
using Animata.Core.Entities;
using Animata.Core.Physics;
using BepuPhysics;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Nieruchomy słupek-przeszkoda (pionowy walec stojący na Body.Position). Kolizje go nie przesuwają, wąsy go widzą.
/// W świecie z fizyką jest statycznym walcem; przesunięty albo zmieniony ręcznie — odtwarza się przed następnym krokiem.
/// </summary>
public sealed class Obstacle() : PhysicalStaticEntity(new Body())
{
    public float Radius { get; set; } = 0.5f;
    public float Height { get; set; } = 0.8f;

    public override float BoundingRadius => Radius;

    public override EntityCategory Category => EntityCategory.Obstacle;

    protected override object Shape => (Body.Position, Radius, Height);

    protected override StaticHandle Build(PhysicsWorld aPhysics) =>
        AddUprightCylinder(aPhysics, Body.Position, MathF.Max(0.01f, Radius), MathF.Max(0.01f, Height));
}

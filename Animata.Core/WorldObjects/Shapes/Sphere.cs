using Animata.Core.Bodies;
using Animata.Core.Entities;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Kula leżąca na Body.Position (środek o promień wyżej). Cel oka stworów (kategoria <see cref="EntityCategory.Target"/>);
/// nie jest bryłą fizyki — stwory przez nią przechodzą.
/// </summary>
public sealed class Sphere() : StaticEntity(new Body())
{
    public float Radius { get; set; } = 0.4f;

    public override float BoundingRadius => Radius;

    public override EntityCategory Category => EntityCategory.Target;
}

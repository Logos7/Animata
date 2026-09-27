using Animata.Core.Bodies;
using Animata.Core.Entities;

namespace Animata.Core.WorldObjects;

/// <summary>Nieruchomy słupek-przeszkoda (walec). Kolizje go nie przesuwają, wąsy go widzą.</summary>
public sealed class Obstacle : StaticEntity
{
    public Obstacle() : base(new Body())
    {
    }

    public float Radius { get; set; } = 0.5f;
    public float Height { get; set; } = 0.8f;

    public override float BoundingRadius => Radius * MathF.Max(Body.Scale.X, Body.Scale.Y);

    public override EntityCategory Category => EntityCategory.Obstacle;
}

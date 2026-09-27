using Animata.Core.Bodies;
using Animata.Core.Entities;

namespace Animata.Core.WorldObjects;

public sealed class TargetBall : StaticEntity
{
    public TargetBall() : base(new Body())
    {
    }

    public float Radius { get; set; } = 0.4f;

    public override float BoundingRadius => Radius * MathF.Max(Body.Scale.X, Body.Scale.Y);

    public override EntityCategory Category => EntityCategory.Target;
}

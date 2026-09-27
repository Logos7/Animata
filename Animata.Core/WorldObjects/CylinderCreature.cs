using System.Numerics;
using Animata.Core.Bodies;
using Animata.Core.Brains;
using Animata.Core.Entities;

namespace Animata.Core.WorldObjects;

public sealed class CylinderCreature : ActiveEntity
{
    public CylinderCreature(Brain? aBrain = null) : base(new Body(), aBrain)
    {
    }

    public float Radius { get; set; } = 0.7f;
    public float Height { get; set; } = 0.3f;

    /// <summary>Kolor ciała (RGB 0–1) — cecha wyglądu, używana przez renderer.</summary>
    public Vector3 Color { get; set; } = new(0.24f, 0.68f, 0.9f);

    public override float BoundingRadius => Radius * MathF.Max(Body.Scale.X, Body.Scale.Y);

    public override EntityCategory Category => EntityCategory.Creature;
}

using System.Numerics;
using Animata.Core.Bodies;
using Animata.Core.Brains;
using Animata.Core.Entities;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Autko: prostopadłościan jadący wzdłuż lokalnej osi X, skręcający przednimi kołami (model rowerowy,
/// patrz <see cref="Actuators.SteeringDriveActuator"/>). Nie obraca się w miejscu.
/// Do kolizji i zmysłów traktowane jako okrąg o promieniu połowy długości.
/// </summary>
public sealed class CarCreature : ActiveEntity
{
    public CarCreature(Brain? aBrain = null) : base(new Body(), aBrain)
    {
    }

    public float Length { get; set; } = 1.2f;
    public float Width { get; set; } = 0.6f;
    public float Height { get; set; } = 0.35f;

    /// <summary>Kolor nadwozia (RGB 0–1) — cecha wyglądu, używana przez renderer.</summary>
    public Vector3 Color { get; set; } = new(0.24f, 0.68f, 0.9f);

    public override float BoundingRadius => 0.5f * MathF.Max(Length * Body.Scale.X, Width * Body.Scale.Y);

    public override EntityCategory Category => EntityCategory.Creature;
}

using System.Numerics;
using Animata.Core.Bodies;
using Animata.Core.Brains;
using Animata.Core.Entities;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Autko w fizyce: nadwozie (pudło L × W × H) na czterech kołach-bryłach z zawieszeniem. Przednie koła skręcają,
/// tylne są napędzane (<see cref="Actuators.SteeringDriveActuator"/> zadaje skręt i prędkość, resztę liczy fizyka:
/// przyspieszenie, poślizg, zderzenia; napęd na cztery koła szorował przednimi i poszerzał skręt). Nie obraca się w miejscu. Dla zmysłów (wąsy, szczelina do celu) to okrąg
/// o promieniu połowy długości.
/// </summary>
public sealed class CarCreature : ArticulatedCreature
{
    public const float DefaultLength = 1.2f;
    public const float DefaultWidth = 0.6f;
    public const float DefaultHeight = 0.35f;
    public const float WheelRadius = 0.13f;
    public const float WheelWidth = 0.1f;

    /// <summary>Oś kół od środka: ±0.32 długości — rozstaw osi 0.77 m.</summary>
    public const float AxleOffset = 0.32f;

    public CarCreature(Brain? aBrain = null) : base(DefaultPlan(), aBrain)
    {
        Color = new Vector3(0.24f, 0.68f, 0.9f);
    }

    public float Length => DefaultLength;
    public float Width => DefaultWidth;
    public float Height => DefaultHeight;

    public override float BoundingRadius => 0.5f * MathF.Max(Length, Width);

    /// <summary>
    /// Nadwozie 3 kg z dołem na wysokości środków kół; koła 0.3 kg (tarcie 1.2) na zawieszeniu 4 cm,
    /// przednie skrętne, tylne napędzane (moment z napędu).
    /// </summary>
    public static BodyPlan DefaultPlan()
    {
        var builder = new BodyPlanBuilder()
            .Part(new PartPlan("Nadwozie", PartShape.Box, new Vector3(DefaultLength, DefaultWidth, DefaultHeight), 3,
                new Vector3(0, 0, WheelRadius + DefaultHeight / 2), Quaternion.Identity, 0.5f));
        foreach (var (name, x, y) in new[]
        {
            ("Koło LP", AxleOffset, 1f), ("Koło PP", AxleOffset, -1f), ("Koło LT", -AxleOffset, 1f), ("Koło PT", -AxleOffset, -1f)
        })
        {
            builder.Part(new PartPlan(name, PartShape.Cylinder, new Vector3(WheelRadius, WheelWidth, 0), 0.3f,
                new Vector3(x * DefaultLength, y * (DefaultWidth / 2 + WheelWidth / 2 + 0.01f), WheelRadius), Quaternion.Identity, 1.2f));
            builder.Wheel($"Oś {name[5..]}", "Nadwozie", name, aSteerable: x > 0, aDriven: x < 0, aTorque: 3);
        }
        return builder.Build();
    }
}

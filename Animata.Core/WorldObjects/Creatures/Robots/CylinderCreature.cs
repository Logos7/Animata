using System.Numerics;
using Animata.Core.Bodies;
using Animata.Core.Brains;
using Animata.Core.Entities;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Walec w fizyce: płaski dysk na dwóch kołach napędzanych po bokach (napęd różnicowy,
/// <see cref="Actuators.DiskDriveActuator"/>) i dwóch kulkach podporowych z przodu i z tyłu (bez tarcia).
/// Obraca się w miejscu. Z przodu ma „nos” — małą kulę, żeby było widać, dokąd patrzy.
/// </summary>
public sealed class CylinderCreature : ArticulatedCreature
{
    public const float DefaultRadius = 0.7f;
    public const float DefaultHeight = 0.3f;
    public const float WheelRadius = 0.12f;

    /// <summary>Koła po bokach, w odległości ±TrackHalf od środka.</summary>
    public const float TrackHalf = 0.55f;

    public CylinderCreature(Brain? aBrain = null) : base(DefaultPlan(), aBrain)
    {
        Color = new Vector3(0.24f, 0.68f, 0.9f);
    }

    public float Radius => DefaultRadius;
    public float Height => DefaultHeight;

    public override float BoundingRadius => Radius;

    public static BodyPlan DefaultPlan()
    {
        const float clearance = 0.08f;
        var upright = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
        var builder = new BodyPlanBuilder()
            .Part(new PartPlan("Dysk", PartShape.Cylinder, new Vector3(DefaultRadius, DefaultHeight, 0), 3,
                new Vector3(0, 0, clearance + DefaultHeight / 2), upright, 0.4f));
        foreach (var (name, side) in new[] { ("Koło L", 1f), ("Koło P", -1f) })
        {
            builder.Part(new PartPlan(name, PartShape.Cylinder, new Vector3(WheelRadius, 0.08f, 0), 0.3f,
                new Vector3(0, side * TrackHalf, WheelRadius), Quaternion.Identity, 1.2f));
            builder.Wheel($"Oś {name[5..]}", "Dysk", name, aSteerable: false, aDriven: true, aTorque: 3);
        }
        foreach (var (name, x) in new[] { ("Podpora P", 0.5f), ("Podpora T", -0.5f) })
        {
            builder.Part(new PartPlan(name, PartShape.Sphere, new Vector3(clearance, 0, 0), 0.1f, new Vector3(x, 0, clearance),
                Quaternion.Identity, 0.02f));
            builder.Weld(name, "Dysk", name);
        }
        builder.Part(new PartPlan("Nos", PartShape.Sphere, new Vector3(0.12f, 0, 0), 0.05f,
            new Vector3(DefaultRadius * 1.02f, 0, clearance + DefaultHeight / 2), Quaternion.Identity, 0));
        builder.Weld("Nos", "Dysk", "Nos");
        return builder.Build();
    }
}

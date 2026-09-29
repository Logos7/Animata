using System.Numerics;
using Animata.Core.Bodies;
using Animata.Core.Brains;
using Animata.Core.Actuators;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Sensors;
using Animata.Core.Training;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Pająk — czworonóg z części: płaski tułów (pudło) i 4 nogi rozstawione po przekątnych (przednia lewa, przednia prawa,
/// tylna lewa, tylna prawa). Noga: udo poziomo na zewnątrz z rogu tułowia, goleń pionowo w dół. Staw biodra zgina udo
/// wokół jego własnych osi: skręt = zamach przód–tył, pochylenie = uniesienie (ujemne) / dociśnięcie; kolano zgina goleń
/// w pionowej płaszczyźnie nogi. Stawy w kolejności: biodro 0, kolano 0, biodro 1, kolano 1, … (8 stawów, porty
/// Yaw{k}/Pitch{k} jak u węża; skręt kolana ciało pomija).
/// Body.Position = ziemia pod środkiem tułowia.
/// </summary>
public sealed class SpiderCreature : ArticulatedCreature
{
    public const int Legs = 4;

    /// <summary>Wysokość środka tułowia nad ziemią w pozie spoczynkowej (goleń sięga ziemi).</summary>
    public const float BodyHeight = 0.22f;

    public const float BodyLength = 0.44f;
    public const float BodyWidth = 0.28f;
    public const float LegRadius = 0.025f;
    public const float ThighLength = 0.16f;
    public const float ShinLength = 0.18f;

    /// <summary>Kierunki nóg (rad, od przodu, w lewo dodatnio): przednia lewa, przednia prawa, tylna lewa, tylna prawa.</summary>
    public static readonly float[] LegAngles = [MathF.PI / 4, -MathF.PI / 4, 3 * MathF.PI / 4, -3 * MathF.PI / 4];

    public static readonly string[] LegNames = ["PL", "PP", "TL", "TP"];

    public SpiderCreature(Brain? aBrain = null) : base(DefaultPlan(), aBrain)
    {
        // Nogi dźwigają tułów: serwa muszą być sztywniejsze niż w wężu (sprężyna Bepu skaluje się z masą części).
        ServoFrequency = 60;
    }

    /// <summary>Obrys do zmysłów i szczeliny do celu: pół rozstawu nóg.</summary>
    public override float BoundingRadius => 0.3f;

    /// <summary>
    /// Oko „Eye” (tułów), czucie stawów „Joints” (8: biodro, kolano × 4), zegar „Clock” (2.5 Hz), czucie terenu „Feel”,
    /// dotyk „Touch” (stopy i brzuch), nogi „Legs”.
    /// </summary>
    public override void Equip()
    {
        Body.Sensors.Add(new TargetSensor { Slot = "Eye" });
        Body.Sensors.Add(new JointSensor(GaitModule.Joints) { Slot = "Joints" });
        Body.Sensors.Add(new ClockSensor { Slot = "Clock", Frequency = 2.5f });
        Body.Sensors.Add(new FeelSensor { Slot = "Feel" });
        Body.Sensors.Add(WorldObjectCatalog.CreateSpiderTouch());
        Body.Actuators.Add(new SpineActuator(GaitModule.Joints) { Slot = "Legs" });
        base.Equip();
    }

    public override IReadOnlyList<BrainPreset> BrainPresets =>
    [
        new("Sieć neuronowa", "zegar rytmu i cel, 2 warstwy ukryte, losowe wagi",
            () => WorldObjectCatalog.CreateSpiderNeuralModule(false, WorldObjectCatalog.DefaultSpiderHidden)),
        new("Generator chodu (kłus)", "6 parametrów: krok, uniesienie, kolano, częstotliwość, skręt", () => new GaitModule { Name = "Chód" }, true)
    ];

    public override SeekRig TrainingRig => SeekRigs.Spider;

    public override string Describe() => $"SpiderCreature · {Legs} nogi · {JointCount} stawów · fizyka Bepu";

    public static BodyPlan DefaultPlan()
    {
        var height = BodyHeight;
        var builder = new BodyPlanBuilder()
            .Part(new PartPlan("Tułów", PartShape.Box, new Vector3(BodyLength, BodyWidth, 0.1f), 0.6f, new Vector3(0, 0, height),
                Quaternion.Identity, 0.4f));
        var hips = new Vector3[Legs];
        var knees = new Vector3[Legs];
        for (var leg = 0; leg < Legs; leg++)
        {
            var angle = LegAngles[leg];
            var direction = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);
            var along = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle);
            var hip = new Vector3(MathF.Sign(direction.X) * (BodyLength / 2 - 0.03f), MathF.Sign(direction.Y) * (BodyWidth / 2 - 0.03f), height);
            var thighSpan = ThighLength + 2 * LegRadius;
            var knee = hip + direction * (thighSpan + 0.02f);
            hips[leg] = hip;
            knees[leg] = knee;
            var name = LegNames[leg];
            // Udo poziomo na zewnątrz (oś X części wzdłuż nogi — staw biodra: skręt = zamach przód–tył, pochylenie = uniesienie).
            builder.Part(new PartPlan($"Udo{name}", PartShape.Capsule, new Vector3(LegRadius, ThighLength, 0), 0.25f,
                hip + direction * (0.01f + thighSpan / 2), along, 0.4f));
            // Goleń: oś X części w dół (obrót o 90° wokół Y), górna półkula przy kolanie, dolna na ziemi.
            builder.Part(new PartPlan($"Goleń{name}", PartShape.Capsule, new Vector3(LegRadius, ShinLength, 0), 0.2f,
                knee - new Vector3(0, 0, ShinLength / 2 + LegRadius - 0.01f),
                along * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), 1.0f));
        }
        for (var leg = 0; leg < Legs; leg++)
        {
            var name = LegNames[leg];
            builder.Joint($"Biodro{leg}", "Tułów", $"Udo{name}", hips[leg], aMaxYaw: 0.7f, aMaxPitch: 0.7f, aStrength: 6);
            builder.Joint($"Kolano{leg}", $"Udo{name}", $"Goleń{name}", knees[leg], aMaxYaw: 0, aMaxPitch: 1.0f, aStrength: 5);
        }
        return builder.Build();
    }
}

using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Bodies;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Sensors;
using Animata.Core.Training;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Humanoid — dwunóg ok. 1.4 m z tułowiem, głową, rękami i nogami (18 ruchomych osi). Kończyny wiszą w dół: oś X członu
/// w dół, Y w lewo, Z do przodu — więc w każdym stawie kulowym pochylenie (oś Y) to zgięcie w przód–tył, a skręt (oś Z)
/// to odwiedzenie na bok. Dodatnie pochylenie przesuwa dolny koniec członu do tyłu.
/// Stawy (kolejność = porty):
/// - talia (miednica → tułów): skłon ±0.5, przechył ±0.3;
/// - biodro L/P: zgięcie −1.4 (noga do przodu) … +0.5 (do tyłu), odwiedzenie −0.3 … +0.4 (na zewnątrz);
/// - kolano L/P: tylko zgięcie 0 … +2.2 (goleń do tyłu) — nie wygina się wstecz;
/// - kostka L/P: zgięcie ±0.6, przechył ±0.35;
/// - bark L/P: zamach ±1.5, odwiedzenie −0.2 … +1.5; łokieć L/P: tylko zgięcie −2.2 … 0 (przedramię do przodu);
/// - szyja: sztywna.
/// Korzeń to miednica (oko, błędnik i obrys liczą się od niej). Gniazda: oko „Eye”, czucie stawów „Joints”, zegar kroku
/// „Clock” (1 Hz), błędnik „Balance”, dotyk „Touch” (stopy, tułów — upadek), napęd stawów „Joints” — tu „Body”.
/// </summary>
public static class Humanoid
{
    public const float FootLength = 0.24f;
    public const float FootWidth = 0.11f;
    public const float FootHeight = 0.05f;
    public const float HipWidth = 0.1f;

    public const float AnkleZ = 0.08f;
    public const float KneeZ = 0.44f;
    public const float HipZ = 0.8f;
    public const float WaistZ = 0.94f;
    public const float ShoulderZ = 1.3f;
    public const float ShoulderY = 0.22f;
    public const float ElbowZ = 1.02f;

    /// <summary>Wysokość, poniżej której miednica oznacza upadek (w pozie spoczynkowej ok. 0.87 m).</summary>
    public const float FallenHeight = 0.55f;

    public static readonly string[] Sides = ["L", "P"];

    /// <summary>Strony z kierunkiem osi Y: lewa +1, prawa −1.</summary>
    private static readonly (string Side, float Sign)[] SideSigns = [("L", 1f), ("P", -1f)];

    // Indeksy stawów w planie (kolejność dodawania).
    public const int Waist = 0;
    public static int Hip(int aSide) => 1 + 3 * aSide;
    public static int Knee(int aSide) => 2 + 3 * aSide;
    public static int Ankle(int aSide) => 3 + 3 * aSide;
    public static int Shoulder(int aSide) => 7 + 2 * aSide;
    public static int Elbow(int aSide) => 8 + 2 * aSide;

    /// <summary>Plan ciała — jeden na proces.</summary>
    public static BodyPlan Plan { get; } = DefaultPlan();

    /// <summary>Porty napędu stawów „Body” (ruchome osie planu): najpierw skręty (Yaw), potem pochylenia (Pitch).</summary>
    public static readonly string[] Ports = JointPorts.Driven(Plan);

    /// <summary>Zmysły humanoida (wspólne z <see cref="MuscleHumanoid"/>); dotyk: stopy i tułów (tułów na ziemi = upadek).</summary>
    internal static readonly SlotSpec[] Senses =
    [
        new SlotSpec("Eye", nameof(TargetSensor)),
        new SlotSpec("Joints", nameof(JointSensor)),
        new SlotSpec("Clock", nameof(ClockSensor)) { Settings = SlotSpec.Values((nameof(ClockSensor.Frequency), 1f)) },
        new SlotSpec("Balance", nameof(BalanceSensor)),
        new SlotSpec("Touch", nameof(TouchSensor))
        {
            Touch = [new TouchPoint("StopaL", "StopaL"), new TouchPoint("StopaP", "StopaP"), new TouchPoint("Tułów", "Tułów")]
        }
    ];

    private static readonly CreatureBlueprint Body = new(Plan, Senses,
        [new SlotSpec("Body", nameof(SpineActuator))])
    {
        ServoFrequency = 60,
        BoundingRadius = 0.3f
    };

    public static CreatureDesign Design { get; } = new()
    {
        Id = "humanoid",
        Name = "Humanoid",
        Icon = "humanoid",
        Blueprint = _ => Body,
        Presets = aHumanoid => HumanoidBrains.Presets(aHumanoid),
        TrainingRig = _ => SeekRigs.HumanoidWalk,
        // Stanie uczy się na próbach z pchnięciami (bez celu), wszystko inne — na dojściu do celu.
        ModuleRig = (_, aModule) => aModule is BalanceModule || aModule.Name == HumanoidBrains.StandName
            ? SeekRigs.HumanoidStand
            : SeekRigs.HumanoidWalk,
        Describe = aHumanoid => $"Humanoid · {aHumanoid.Plan.Parts.Count} części · {aHumanoid.JointCount} stawów · fizyka Bepu"
    };

    /// <summary>
    /// Zła postawa (0 dobra … 1 zła): 1, gdy miednica jest niżej niż <see cref="FallenHeight"/> albo tułów dotyka ziemi;
    /// pomiędzy — rośnie z pochyleniem miednicy od 15° do 45°.
    /// </summary>
    public static float Posture(ActiveEntity aCreature)
    {
        if (aCreature is not ArticulatedCreature body || body.PartPositions.Count == 0)
            return 0;
        if (body.PartPositions[0].Z < FallenHeight || body.IsPartTouching(body.Plan.IndexOf("Tułów")))
            return 1;
        var up = Vector3.Transform(Vector3.UnitZ, body.PartOrientations[0]).Z;
        return Math.Clamp((0.966f - up) / (0.966f - 0.707f), 0, 1);
    }

    public static BodyPlan DefaultPlan(float aFootMass = 2f, float aFootX = 0.02f, float aShinMass = 1.5f)
    {
        // Kończyna wisząca w dół: oś X członu w dół, Z do przodu.
        var down = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        var builder = new BodyPlanBuilder()
            .Part(new PartPlan("Miednica", PartShape.Box, new Vector3(0.18f, 0.3f, 0.12f), 3, new Vector3(0, 0, HipZ + 0.07f), Quaternion.Identity, 0.5f))
            .Part(new PartPlan("Tułów", PartShape.Box, new Vector3(0.34f, 0.32f, 0.18f), 5, new Vector3(0, 0, WaistZ + 0.18f), down, 0.5f))
            .Part(new PartPlan("Głowa", PartShape.Sphere, new Vector3(0.1f, 0, 0), 1.2f, new Vector3(0, 0, 1.46f), Quaternion.Identity, 0.5f));
        foreach (var (side, sign) in SideSigns)
        {
            var y = sign * HipWidth;
            builder.Part(new PartPlan($"Udo{side}", PartShape.Capsule, new Vector3(0.055f, HipZ - KneeZ - 0.13f, 0), 2,
                new Vector3(0, y, (HipZ + KneeZ) / 2), down, 0.5f));
            builder.Part(new PartPlan($"Goleń{side}", PartShape.Capsule, new Vector3(0.045f, KneeZ - AnkleZ - 0.11f, 0), aShinMass,
                new Vector3(0, y, (KneeZ + AnkleZ) / 2), down, 0.5f));
            builder.Part(new PartPlan($"Stopa{side}", PartShape.Box, new Vector3(FootHeight, FootWidth, FootLength), aFootMass,
                new Vector3(aFootX, y, FootHeight / 2), down, 1.0f));
        }
        foreach (var (side, sign) in SideSigns)
        {
            var y = sign * ShoulderY;
            builder.Part(new PartPlan($"Ramię{side}", PartShape.Capsule, new Vector3(0.04f, ShoulderZ - ElbowZ - 0.1f, 0), 0.7f,
                new Vector3(0, y, (ShoulderZ + ElbowZ) / 2), down, 0.5f));
            builder.Part(new PartPlan($"Przedramię{side}", PartShape.Capsule, new Vector3(0.035f, 0.2f, 0), 0.5f,
                new Vector3(0, y, ElbowZ - 0.15f), down, 0.5f));
        }

        builder.Joint("Talia", "Miednica", "Tułów", new Vector3(0, 0, WaistZ), aMaxYaw: 0.3f, aMaxPitch: 0.5f, aStrength: 120);
        foreach (var (side, sign) in SideSigns)
        {
            var y = sign * HipWidth;
            // Odwiedzenie: skręt wokół osi Z członu (do przodu) — dla lewej nogi dodatni na zewnątrz, dla prawej ujemny.
            var (abductMin, abductMax) = sign > 0 ? (-0.3f, 0.4f) : (-0.4f, 0.3f);
            builder.Joints([
                new JointPlan($"Biodro{side}", "Miednica", $"Udo{side}", new Vector3(0, y, HipZ), JointKind.Ball, abductMax, 0.5f, 150,
                    MinYaw: abductMin, MinPitch: -1.4f),
                new JointPlan($"Kolano{side}", $"Udo{side}", $"Goleń{side}", new Vector3(0, y, KneeZ), JointKind.Ball, 0, 2.2f, 150,
                    MinYaw: 0, MinPitch: 0),
                new JointPlan($"Kostka{side}", $"Goleń{side}", $"Stopa{side}", new Vector3(0, y, AnkleZ), JointKind.Ball, 0.35f, 0.6f, 100)
            ]);
        }
        foreach (var (side, sign) in SideSigns)
        {
            var y = sign * ShoulderY;
            var (abductMin, abductMax) = sign > 0 ? (-0.2f, 1.5f) : (-1.5f, 0.2f);
            builder.Joints([
                new JointPlan($"Bark{side}", "Tułów", $"Ramię{side}", new Vector3(0, y, ShoulderZ), JointKind.Ball, abductMax, 1.5f, 25,
                    MinYaw: abductMin),
                new JointPlan($"Łokieć{side}", $"Ramię{side}", $"Przedramię{side}", new Vector3(0, y, ElbowZ), JointKind.Ball, 0, 0, 15,
                    MinYaw: 0, MinPitch: -2.2f)
            ]);
        }
        builder.Weld("Szyja", "Tułów", "Głowa");
        return builder.Build();
    }
}

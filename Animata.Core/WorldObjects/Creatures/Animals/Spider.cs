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
/// tylna lewa, tylna prawa). Noga ma trzy segmenty jak u prawdziwego pająka albo robota-kroczaka:
/// biodro (krótki człon poziomo z rogu tułowia), udo (w górę i na zewnątrz) i goleń (w dół do ziemi). Trzy stawy nogi:
/// - zamach (tułów → biodro): tylko skręt wokół pionu, ±0.7 rad — noga do przodu i do tyłu;
/// - uniesienie (biodro → udo): tylko pochylenie, od −0.9 (udo w górę) do +0.4 rad (docisk w dół);
/// - kolano (udo → goleń): tylko pochylenie i tylko w jedną stronę — od 0 (poza spoczynkowa, najbardziej wyprostowana)
///   do +1.3 rad (zgięcie: stopa pod tułów). Kolano nie wygina się wstecz.
/// Stawy w kolejności noga po nodze (zamach n = 3n, uniesienie 3n+1, kolano 3n+2); porty tylko ruchomych osi: Yaw{3n},
/// Pitch{3n+1}, Pitch{3n+2} — 12 portów. Body.Position = ziemia pod środkiem tułowia.
/// Człony są cięższe niż w dawnej nodze z dwóch (biodro 0.3, udo 0.3, goleń 0.25 kg; serwa 10 N·m, 60 Hz): sprężyna serwa
/// Bepu skaluje się z masą części, a lekki człon między tułowiem a udem uginał się i drgał. Pomiar (beta.29, rig pająka,
/// walidacja 8 tras): tułów stoi 13.4 cm nad ziemią (poza 16 cm); ręczny kłus 8/8, koszt 0.342, brzuch na ziemi 3% czasu;
/// nauka kłusa od losowego 10 pok.: 8/8 (ziarno 5), 2/8 (ziarno 3); sieć 5-16-12-12 od losowych wag: 6/8 po 30 pok.
/// </summary>
public static class Spider
{
    public const int Legs = 4;
    public const int JointsPerLeg = 3;
    public const int Joints = Legs * JointsPerLeg;

    /// <summary>Wysokość środka tułowia nad ziemią w pozie spoczynkowej (golenie sięgają ziemi).</summary>
    public const float BodyHeight = 0.16f;

    public const float BodyLength = 0.44f;
    public const float BodyWidth = 0.28f;
    public const float LegRadius = 0.025f;

    /// <summary>Długości walców członów nogi (bez półkul): biodro, udo; goleń sięga od kolana do ziemi.</summary>
    public const float CoxaLength = 0.04f;
    public const float FemurLength = 0.16f;

    /// <summary>Udo w pozie spoczynkowej wznosi się pod tym kątem (rad) — kolano ponad tułowiem, jak u pająka.</summary>
    public const float FemurRise = 35 * MathF.PI / 180;

    /// <summary>Zakresy stawów nogi (rad): zamach ±, uniesienie od w górę do w dół, kolano tylko zgięcie.</summary>
    public const float SwingRange = 0.7f;
    public const float LiftUp = -0.9f;
    public const float LiftDown = 0.4f;
    public const float KneeFlex = 1.3f;

    /// <summary>Stawy nogi <paramref name="aLeg"/>: zamach, uniesienie, kolano (indeksy w planie).</summary>
    public static int SwingJoint(int aLeg) => JointsPerLeg * aLeg;
    public static int LiftJoint(int aLeg) => JointsPerLeg * aLeg + 1;
    public static int KneeJoint(int aLeg) => JointsPerLeg * aLeg + 2;

    /// <summary>Kierunki nóg (rad, od przodu, w lewo dodatnio): przednia lewa, przednia prawa, tylna lewa, tylna prawa.</summary>
    public static readonly float[] LegAngles = [MathF.PI / 4, -MathF.PI / 4, 3 * MathF.PI / 4, -3 * MathF.PI / 4];

    public static readonly string[] LegNames = ["PL", "PP", "TL", "TP"];

    /// <summary>Porty dotyku pająka: stopy (FootPL, FootPP, FootTL, FootTP) i brzuch (Belly).</summary>
    public static readonly string[] TouchPorts = [.. LegNames.Select(aLeg => "Foot" + aLeg), "Belly"];

    /// <summary>
    /// Oko „Eye” (tułów), czucie stawów „Joints” (12 osi: zamach, uniesienie, kolano × 4), zegar „Clock” (2.5 Hz), czucie terenu „Feel”,
    /// dotyk „Touch” (golenie jako stopy i tułów jako brzuch), nogi „Legs”. Nogi dźwigają tułów: serwa są sztywniejsze
    /// niż w wężu (sprężyna Bepu skaluje się z masą części). Obrys: pół rozstawu nóg.
    /// </summary>
    private static readonly CreatureBlueprint Body = new(DefaultPlan(),
        [
            new SlotSpec("Eye", nameof(TargetSensor)),
            new SlotSpec("Joints", nameof(JointSensor)),
            new SlotSpec("Clock", nameof(ClockSensor)) { Settings = SlotSpec.Values((nameof(ClockSensor.Frequency), 2.5f)) },
            new SlotSpec("Feel", nameof(FeelSensor)),
            new SlotSpec("Touch", nameof(TouchSensor))
            {
                Touch = [.. LegNames.Select(aLeg => new TouchPoint("Foot" + aLeg, "Goleń" + aLeg)), new TouchPoint("Belly", "Tułów")]
            }
        ],
        [new SlotSpec("Legs", nameof(SpineActuator))])
    {
        ServoFrequency = 60,
        BoundingRadius = 0.3f
    };

    public static CreatureDesign Design { get; } = new()
    {
        Id = "spider",
        Name = "Pająk",
        Icon = "spider",
        Blueprint = _ => Body,
        Presets = _ =>
        [
            new("Sieć neuronowa", "zegar rytmu i cel, 2 warstwy ukryte, losowe wagi",
                () => WorldObjectCatalog.CreateSpiderNeuralModule(false, WorldObjectCatalog.DefaultSpiderHidden)),
            new("Generator chodu (kłus)", "6 parametrów: krok, uniesienie, zgięcie kolana, częstotliwość, skręt", () => new GaitModule { Name = "Chód" }, true)
        ],
        TrainingRig = _ => SeekRigs.Spider,
        Describe = aSpider => $"Pająk · {Legs} nogi · {aSpider.JointCount} stawów · fizyka Bepu"
    };

    /// <summary>
    /// Zła postawa pająka (kara w nauce): 1, gdy tułów leży na ziemi albo pająk jest przewrócony (tułów pochylony o 60°
    /// i więcej), pomiędzy — rośnie z przechyleniem.
    /// </summary>
    public static float Posture(ActiveEntity aCreature)
    {
        if (!Design.Is(aCreature) || aCreature is not Creature { PartOrientations.Count: > 0 } spider)
            return 0;
        if (spider.IsPartTouching(0))
            return 1;
        var up = Vector3.Transform(Vector3.UnitZ, spider.PartOrientations[0]).Z;
        return Math.Clamp((0.95f - up) / 0.45f, 0, 1);
    }

    public static BodyPlan DefaultPlan(float aCoxaMass = 0.3f, float aFemurMass = 0.3f, float aShinMass = 0.25f, float aStrength = 10, float aKneeStrength = 10)
    {
        var builder = new BodyPlanBuilder()
            .Part(new PartPlan("Tułów", PartShape.Box, new Vector3(BodyLength, BodyWidth, 0.1f), 0.6f, new Vector3(0, 0, BodyHeight),
                Quaternion.Identity, 0.4f));
        var coxaSpan = CoxaLength + 2 * LegRadius;
        var femurSpan = FemurLength + 2 * LegRadius;
        var anchors = new (Vector3 Hip, Vector3 Lift, Vector3 Knee)[Legs];
        for (var leg = 0; leg < Legs; leg++)
        {
            var angle = LegAngles[leg];
            var direction = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);
            var along = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle);
            var hip = new Vector3(MathF.Sign(direction.X) * (BodyLength / 2 - 0.03f), MathF.Sign(direction.Y) * (BodyWidth / 2 - 0.03f), BodyHeight);
            var lift = hip + direction * (coxaSpan + 0.02f);
            var up = direction * MathF.Cos(FemurRise) + Vector3.UnitZ * MathF.Sin(FemurRise);
            var knee = lift + up * (femurSpan + 0.01f);
            anchors[leg] = (hip, lift, knee);
            var name = LegNames[leg];
            // Biodro: krótki człon poziomo na zewnątrz (oś X części wzdłuż nogi).
            builder.Part(new PartPlan($"Biodro{name}", PartShape.Capsule, new Vector3(LegRadius, CoxaLength, 0), aCoxaMass,
                hip + direction * (0.01f + coxaSpan / 2), along, 0.4f));
            // Udo: w górę i na zewnątrz (obrót o −FemurRise wokół osi Y części — dodatnie pochylenie opuszcza oś X).
            builder.Part(new PartPlan($"Udo{name}", PartShape.Capsule, new Vector3(LegRadius, FemurLength, 0), aFemurMass,
                lift + up * (femurSpan / 2 + 0.005f), along * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -FemurRise), 0.4f));
            // Goleń: oś X części w dół, od kolana do ziemi.
            var shinSpan = knee.Z - 0.01f;
            builder.Part(new PartPlan($"Goleń{name}", PartShape.Capsule, new Vector3(LegRadius, shinSpan - 2 * LegRadius, 0), aShinMass,
                new Vector3(knee.X, knee.Y, shinSpan / 2), along * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), 1.0f));
        }
        for (var leg = 0; leg < Legs; leg++)
        {
            var name = LegNames[leg];
            var (hip, lift, knee) = anchors[leg];
            builder.Swivel($"Zamach{leg}", "Tułów", $"Biodro{name}", hip, -SwingRange, SwingRange, aStrength);
            builder.Hinge($"Uniesienie{leg}", $"Biodro{name}", $"Udo{name}", lift, LiftUp, LiftDown, aStrength);
            // Dodatnie pochylenie goleni (oś X w dół) przesuwa stopę do środka — zgięcie; wstecz kolano się nie wygina.
            builder.Hinge($"Kolano{leg}", $"Udo{name}", $"Goleń{name}", knee, 0, KneeFlex, aKneeStrength);
        }
        return builder.Build();
    }
}

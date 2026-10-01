using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Bodies;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Sensors;
using Animata.Core.Training;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Humanoid mięśniowy — ciało jak <see cref="Humanoid"/>, ale nogi nie mają serw: biodra, kolana i kostki to stawy bierne
/// (<see cref="JointKind.Passive"/>) z twardymi ogranicznikami, a ruszają nimi mięśnie (model Hilla, <see cref="MusclePlan"/>),
/// jak w pracy Geijtenbeeka, van de Panne'a i van der Stappena (2013). Tułów (talia) i ręce zostają na serwach.
/// Na nogę 12 mięśni, w tym dwustawowe:
/// - biodro: biodrowo-lędźwiowy (zgina), pośladkowy wielki (prostuje), pośladkowy średni (odwodzi), przywodziciel;
/// - biodro i kolano: kulszowo-goleniowe (prostują biodro, zginają kolano), prosty uda (zgina biodro, prostuje kolano);
/// - kolano: obszerne (prostują);
/// - kolano i kostka: brzuchaty łydki (zgina kolano, opuszcza palce);
/// - kostka: płaszczkowaty (opuszcza palce), piszczelowy przedni (unosi palce), strzałkowy (nawraca — podnosi
///   zewnętrzną krawędź stopy), piszczelowy tylny (odwraca).
/// Gniazda: oko „Eye”, czucie stawów „Joints” (także biernych), zegar „Clock” (1 Hz), błędnik „Balance”, dotyk
/// „Touch”, czucie mięśni „MuscleSense”; napędy: serwa tułowia i rąk „Body”, mięśnie „Muscles”.
/// </summary>
public static class MuscleHumanoid
{
    /// <summary>Plan ciała — jeden na proces (porty, gniazda i sterowniki mięśniowe go współdzielą).</summary>
    public static BodyPlan Plan { get; } = DefaultPlan();

    /// <summary>Ramiona sił mięśni w pozie spoczynkowej (<see cref="Plan"/>) — wspólne dla sterowników mięśniowych.</summary>
    public static MuscleGeometry Geometry { get; } = new(Plan);

    /// <summary>Mięsień nogi po nazwie i stronie (0 — lewa, 1 — prawa), np. „PośladkowyL”. Stawy — jak w <see cref="Humanoid"/>.</summary>
    public static string Muscle(string aName, int aSide) => aName + Humanoid.Sides[aSide];

    /// <summary>Porty serw (talia, barki, łokcie).</summary>
    public static readonly string[] ServoPorts = SpineActuator.PortsFor(Plan);

    /// <summary>Porty mięśni (pobudzenia), lewa noga, potem prawa.</summary>
    public static readonly string[] MusclePorts = [.. Plan.MuscleList.Select(aMuscle => aMuscle.Name)];

    private static readonly CreatureBlueprint Body = new(Plan,
        [
            new SlotSpec("Eye", nameof(TargetSensor)),
            new SlotSpec("Joints", nameof(JointSensor)),
            new SlotSpec("Clock", nameof(ClockSensor)) { Settings = SlotSpec.Values((nameof(ClockSensor.Frequency), 1f)) },
            new SlotSpec("Balance", nameof(BalanceSensor)),
            new SlotSpec("Touch", nameof(TouchSensor))
            {
                Touch = [new TouchPoint("StopaL", "StopaL"), new TouchPoint("StopaP", "StopaP"), new TouchPoint("Tułów", "Tułów")]
            },
            new SlotSpec("MuscleSense", nameof(MuscleSensor))
        ],
        [new SlotSpec("Body", nameof(SpineActuator)), new SlotSpec("Muscles", nameof(MuscleActuator))])
    {
        ServoFrequency = 60,
        BoundingRadius = 0.3f
    };

    public static CreatureDesign Design { get; } = new()
    {
        Id = "muscleHumanoid",
        Name = "Humanoid mięśniowy",
        Icon = "humanoid",
        Blueprint = _ => Body,
        Presets = aCreature => MuscleHumanoidBrains.Presets(aCreature),
        TrainingRig = _ => SeekRigs.MuscleWalk,
        ModuleRig = (_, aModule) => aModule.Name == MuscleHumanoidBrains.StandName ? SeekRigs.MuscleStand : SeekRigs.MuscleWalk,
        Describe = aCreature => $"Humanoid mięśniowy · {aCreature.MuscleCount} mięśni · {aCreature.JointCount} stawów · fizyka Bepu"
    };

    /// <summary>Tłumienie stawów nóg (N·m·s/rad) — tkanki i maź stawowa.</summary>
    public const float JointDamping = 4;

    /// <summary>Ciało humanoida z nogami na mięśniach (części jak w <see cref="Humanoid.DefaultPlan"/>).</summary>
    public static BodyPlan DefaultPlan()
    {
        var servo = Humanoid.DefaultPlan();
        var builder = new BodyPlanBuilder();
        foreach (var part in servo.Parts)
            builder.Part(part);
        foreach (var joint in servo.Joints)
        {
            var leg = joint.Name.StartsWith("Biodro", StringComparison.Ordinal) || joint.Name.StartsWith("Kolano", StringComparison.Ordinal) ||
                      joint.Name.StartsWith("Kostka", StringComparison.Ordinal);
            builder.Joints([leg ? joint with { Kind = JointKind.Passive, Strength = JointDamping } : joint]);
        }
        for (var side = 0; side < 2; side++)
            foreach (var muscle in LegMusclesOf(side))
                builder.Muscle(muscle);
        return builder.Build();
    }

    /// <summary>
    /// Mięśnie jednej nogi. Punkty dla lewej nogi (Y w lewo = na zewnątrz); prawa — lustro w Y. Biodro w (0, ±0.1, 0.8),
    /// kolano 0.44, kostka 0.08; stopa od x −0.10 (pięta) do +0.14. Ramiona sił 3–7 cm; siły dobrane do ciała ok. 23 kg.
    /// </summary>
    public static IEnumerable<MusclePlan> LegMusclesOf(int aSide)
    {
        var s = Humanoid.Sides[aSide];
        var mirror = aSide == 0 ? 1f : -1f;
        Vector3 P(float aX, float aY, float aZ) => new(aX, mirror * aY, aZ);
        string Pelvis = "Miednica", Thigh = $"Udo{s}", Shin = $"Goleń{s}", Foot = $"Stopa{s}";
        yield return new MusclePlan($"Biodrowy{s}", Pelvis, P(0.07f, 0.07f, 0.9f), Thigh, P(0.05f, 0.09f, 0.7f), 600);
        yield return new MusclePlan($"Pośladkowy{s}", Pelvis, P(-0.09f, 0.08f, 0.88f), Thigh, P(-0.05f, 0.11f, 0.68f), 900);
        yield return new MusclePlan($"PośladkowyŚr{s}", Pelvis, P(0, 0.15f, 0.92f), Thigh, P(0, 0.16f, 0.72f), 600);
        yield return new MusclePlan($"Przywodziciel{s}", Pelvis, P(0, 0.03f, 0.82f), Thigh, P(0, 0.06f, 0.58f), 500);
        yield return new MusclePlan($"Kulszowy{s}", Pelvis, P(-0.08f, 0.09f, 0.83f), Shin, P(-0.045f, 0.1f, 0.38f), 700);
        yield return new MusclePlan($"ProstyUda{s}", Pelvis, P(0.07f, 0.1f, 0.87f), Shin, P(0.06f, 0.1f, 0.4f), 500);
        yield return new MusclePlan($"Obszerny{s}", Thigh, P(0.055f, 0.1f, 0.62f), Shin, P(0.06f, 0.1f, 0.4f), 1200);
        yield return new MusclePlan($"Brzuchaty{s}", Thigh, P(-0.05f, 0.1f, 0.5f), Foot, P(-0.1f, 0.1f, 0.04f), 600);
        yield return new MusclePlan($"Płaszczkowaty{s}", Shin, P(-0.045f, 0.1f, 0.3f), Foot, P(-0.1f, 0.1f, 0.04f), 1000);
        yield return new MusclePlan($"PiszczelowyP{s}", Shin, P(0.045f, 0.1f, 0.3f), Foot, P(0.09f, 0.1f, 0.05f), 400);
        yield return new MusclePlan($"Strzałkowy{s}", Shin, P(0, 0.145f, 0.3f), Foot, P(0, 0.15f, 0.04f), 400);
        yield return new MusclePlan($"PiszczelowyT{s}", Shin, P(-0.01f, 0.055f, 0.3f), Foot, P(0, 0.05f, 0.04f), 400);
    }
}

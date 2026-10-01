using System.Collections.Concurrent;
using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Core.Training;

/// <summary>
/// „Ciało do treningu”: jak zbudować stwora z danym sterownikiem i jak liczyć wysiłek z jego komend.
/// <see cref="PrepareWorld"/> dokłada do świata próby to, czego ciało potrzebuje (np. podłogę dla stwora w fizyce).
/// <see cref="Posture"/> — zła postawa w danej chwili (0 = dobra, 1 = zła), karana z wagą <see cref="SeekTargetOptions.PostureWeight"/>.
/// <see cref="Setup"/> — własne ustawienie próby (np. pień, cel na jego szczycie, wąż owinięty wokół niego) zamiast
/// zwykłego: stwór w (0, 0) obrócony o Yaw próby.
/// </summary>
public sealed record SeekRig(
    string Name,
    Func<Guid, BrainModule, ActiveEntity> CreateCreature,
    Func<IReadOnlyDictionary<string, float>, float> Effort,
    SeekTargetOptions DefaultOptions,
    Action<World>? PrepareWorld = null,
    Func<ActiveEntity, float>? Posture = null,
    Action<World, Sphere, ActiveEntity, SeekEpisode>? Setup = null)
{
    /// <summary>Algorytm nauki na tym rigu (domyślnie genetyczny; mięśniowe ciała — CMA-ES).</summary>
    public EvolutionAlgorithm Algorithm { get; init; } = EvolutionAlgorithm.Genetic;
}

public static class SeekRigs
{
    /// <summary>Wysiłek stwora ze stawami: średnia wielkość komend (każda przycięta do 1).</summary>
    private static float AverageCommand(IReadOnlyDictionary<string, float> aCommand)
    {
        if (aCommand.Count == 0)
            return 0;
        var total = 0f;
        foreach (var value in aCommand.Values)
            total += MathF.Min(MathF.Abs(value), 1);
        return total / aCommand.Count;
    }

    /// <summary>Podłoga prób: wszystkie stwory są bryłami w fizyce, więc muszą na czymś stać.</summary>
    private static void AddFloor(World aWorld) => aWorld.Add(WorldObjectCatalog.CreateFloor(60, 60));

    /// <summary>Walec z napędem różnicowym, bez przeszkód.</summary>
    public static readonly SeekRig Disk = new(
        "walec",
        (aTargetId, aController) => WorldObjectCatalog.Create(Disc.Design, Vector3.Zero, 0, aTargetId, aController, Vector3.One),
        aCommand => Math.Clamp(aCommand.GetValueOrDefault(DiskDriveActuator.StepPort), 0, 1)
            + 0.25f * MathF.Min(MathF.Abs(aCommand.GetValueOrDefault(DiskDriveActuator.TurnPort)), 1),
        new SeekTargetOptions(),
        AddFloor);

    private static readonly ConcurrentDictionary<int, SeekRig> CarRigs = new();

    /// <summary>Autko z domyślną liczbą wąsów (<see cref="WorldObjects.Car.DefaultWhiskers"/>).</summary>
    public static SeekRig Car => CarWith(WorldObjects.Car.DefaultWhiskers);

    /// <summary>
    /// Autko z <paramref name="aWhiskers"/> wąsami, 1–3 cylindry na drodze, cel 5–9 m, dłuższe próby
    /// (autko nie skręca w miejscu). Ciało w rigu ma tyle wąsów, ile sieć ma wejść Ray{i}.
    /// </summary>
    public static SeekRig CarWith(int aWhiskers)
    {
        WorldObjects.Car.CheckWhiskerCount(aWhiskers);
        return CarRigs.GetOrAdd(aWhiskers, CreateCarRig);
    }

    private static SeekRig CreateCarRig(int aWhiskers) => new(
        aWhiskers == WorldObjects.Car.DefaultWhiskers ? "autko" : $"autko ×{aWhiskers}",
        (aTargetId, aController) =>
            WorldObjectCatalog.CreateCar(Vector3.Zero, 0, Vector3.One, aTargetId, aController, aWhiskers),
        aCommand => MathF.Min(MathF.Abs(aCommand.GetValueOrDefault(SteeringDriveActuator.ThrottlePort)), 1)
            + 0.25f * MathF.Min(MathF.Abs(aCommand.GetValueOrDefault(SteeringDriveActuator.SteerPort)), 1),
        new SeekTargetOptions
        {
            EpisodeSeconds = 12,
            MinDistance = 5,
            MaxDistance = 9,
            MinObstacles = 1,
            MaxObstacles = 3,
            ContactWeight = 2
        },
        AddFloor);

    private static readonly ConcurrentDictionary<int, SeekRig> SnakeRigs = new();

    /// <summary>
    /// Wąż z <paramref name="aSegments"/> segmentami w fizyce: podłoga 60 × 60 m, 0–3 płaskie klocki terenu (3–10 cm)
    /// na drodze, bez cylindrów, cel 3–6 m (także na klocku), 12 s. Wysiłek = średnia wielkość komend stawów.
    /// </summary>
    public static SeekRig SnakeWith(int aSegments)
    {
        Snake.CheckLength(aSegments);
        return SnakeRigs.GetOrAdd(aSegments, CreateSnakeRig);
    }

    private static SeekRig CreateSnakeRig(int aSegments) => new(
        $"wąż ×{aSegments}",
        (aTargetId, aController) =>
            WorldObjectCatalog.CreateSnake(Vector3.Zero, 0, Vector3.One, aTargetId, aController, aSegments),
        AverageCommand,
        new SeekTargetOptions
        {
            EpisodesPerGeneration = 6,
            EpisodeSeconds = 12,
            MinDistance = 3,
            MaxDistance = 6,
            ValidationEpisodes = 8,
            MaxSlabs = 3,
            BodyLength = aSegments * Snake.SegmentSpacing
        },
        AddFloor);

    private static readonly ConcurrentDictionary<int, SeekRig> ClimbRigs = new();

    /// <summary>Promień pnia w próbach wspinania.</summary>
    public const float ClimbTrunkRadius = 0.25f;

    /// <summary>Kula na szczycie pnia — mała, żeby głowa owinięta tuż pod szczytem była „przy niej”.</summary>
    public const float ClimbTargetRadius = 0.2f;

    /// <summary>Tarcie chwytne pnia (kora).</summary>
    public const float ClimbTrunkGrip = 1.2f;

    /// <summary>Kolor pnia.</summary>
    public static readonly Vector3 TrunkColor = new(0.45f, 0.32f, 0.2f);

    /// <summary>Pień do wspinania: cylinder r 0.25 m z tarciem chwytnym, stojący na (x, y, z).</summary>
    public static Cylinder CreateClimbCylinder(Vector3 aPosition, float aHeight) => new()
    {
        Radius = ClimbTrunkRadius,
        Height = aHeight,
        Grip = ClimbTrunkGrip,
        Color = TrunkColor,
        Body = { Position = aPosition }
    };

    /// <summary>
    /// Wspinaczka węża z <paramref name="aSegments"/> segmentami: pień r 0.25 m o wysokości 2–3.5 m (długość
    /// odcinka próby), cel na szczycie, wąż na starcie owinięty wokół podstawy (zwój obrócony o Yaw próby), 12 s.
    /// </summary>
    public static SeekRig ClimbWith(int aSegments)
    {
        Snake.CheckLength(aSegments);
        return ClimbRigs.GetOrAdd(aSegments, aCount => new SeekRig(
            $"wspinaczka ×{aCount}",
            (aTargetId, aController) => WorldObjectCatalog.CreateSnake(Vector3.Zero, 0, Vector3.One, aTargetId, aController, aCount),
            AverageCommand,
            new SeekTargetOptions
            {
                EpisodesPerGeneration = 4,
                EpisodeSeconds = 12,
                MinDistance = 2,
                MaxDistance = 3.5f,
                ValidationEpisodes = 6
            },
            AddFloor,
            null,
            ClimbSetup));
    }

    /// <summary>Pień w (0, 0) o wysokości = odległość celu w próbie, cel na szczycie, wąż owinięty u podstawy.</summary>
    private static void ClimbSetup(World aWorld, Sphere aTarget, ActiveEntity aCreature, SeekEpisode aEpisode)
    {
        var tree = CreateClimbCylinder(Vector3.Zero, aEpisode.TargetOffset.Length());
        aWorld.Add(tree);
        aTarget.Radius = ClimbTargetRadius;
        aTarget.Body.Position = new Vector3(0, 0, tree.Height);
        if (aCreature is Creature snake && Snake.Design.Is(snake))
            Snake.WrapAround(snake, tree, aEpisode.Yaw);
    }

    /// <summary>
    /// Pająk (czworonóg) w fizyce: podłoga 60 × 60 m, 0–2 niskie klocki (2–6 cm) na drodze, cel 2–5 m, 12 s.
    /// Wysiłek = średnia wielkość komend stawów.
    /// </summary>
    public static readonly SeekRig Spider = new(
        "pająk",
        (aTargetId, aController) => WorldObjectCatalog.Create(WorldObjects.Spider.Design, Vector3.Zero, 0, aTargetId, aController, Vector3.One),
        AverageCommand,
        new SeekTargetOptions
        {
            EpisodesPerGeneration = 6,
            EpisodeSeconds = 12,
            MinDistance = 2,
            MaxDistance = 5,
            ValidationEpisodes = 8,
            MaxSlabs = 2,
            MinSlabHeight = 0.02f,
            MaxSlabHeight = 0.06f,
            BodyLength = 0.4f,
            PostureWeight = 1
        },
        AddFloor,
        WorldObjects.Spider.Posture);

    /// <summary>
    /// Humanoid stoi w miejscu: 10 s, w 1.5 s pchnięcie 0.3–1.0 m/s w losowym kierunku (bez sterowania stoi do ok. 0.4 m/s);
    /// liczy się postawa (upadek, pochylenie), odejście z miejsca i wysiłek — dojście do celu nie (DistanceWeight 0).
    /// </summary>
    public static readonly SeekRig HumanoidStand = new(
        "humanoid · stanie",
        (aTargetId, aController) => WorldObjectCatalog.Create(Humanoid.Design, Vector3.Zero, 0, aTargetId, aController),
        AverageCommand,
        new SeekTargetOptions
        {
            EpisodesPerGeneration = 6,
            EpisodeSeconds = 10,
            MinDistance = 2,
            MaxDistance = 4,
            ValidationEpisodes = 8,
            DistanceWeight = 0,
            DriftWeight = 1,
            PushAtSeconds = 1.5f,
            MinPushSpeed = 0.3f,
            MaxPushSpeed = 1.0f,
            PostureWeight = 1
        },
        AddFloor,
        Humanoid.Posture);

    /// <summary>Humanoid idzie do celu 2–5 m, 12 s, kara za upadek i pochylenie.</summary>
    public static readonly SeekRig HumanoidWalk = new(
        "humanoid · chód",
        (aTargetId, aController) => WorldObjectCatalog.Create(Humanoid.Design, Vector3.Zero, 0, aTargetId, aController),
        AverageCommand,
        new SeekTargetOptions
        {
            EpisodesPerGeneration = 6,
            EpisodeSeconds = 12,
            MinDistance = 2,
            MaxDistance = 5,
            ValidationEpisodes = 8,
            BodyLength = 0.3f,
            PostureWeight = 1
        },
        AddFloor,
        Humanoid.Posture);

    /// <summary>Humanoid mięśniowy stoi w miejscu: jak <see cref="HumanoidStand"/> (10 s, pchnięcie 0.3–1 m/s, kara za odejście).</summary>
    public static readonly SeekRig MuscleStand = HumanoidStand with
    {
        Name = "humanoid mięśniowy · stanie",
        Algorithm = EvolutionAlgorithm.CmaEs,
        CreateCreature = (aTargetId, aController) => WorldObjectCatalog.Create(MuscleHumanoid.Design, Vector3.Zero, 0, aTargetId, aController)
    };

    /// <summary>Humanoid mięśniowy idzie do celu: jak <see cref="HumanoidWalk"/> (12 s, cel 2–5 m).</summary>
    public static readonly SeekRig MuscleWalk = HumanoidWalk with
    {
        Name = "humanoid mięśniowy · chód",
        Algorithm = EvolutionAlgorithm.CmaEs,
        CreateCreature = (aTargetId, aController) => WorldObjectCatalog.Create(MuscleHumanoid.Design, Vector3.Zero, 0, aTargetId, aController)
    };

    /// <summary>
    /// Rig pasujący do ciała stwora (autko — z tą samą liczbą wąsów, wąż — z tą samą liczbą segmentów). Ustawienia zmysłów
    /// i napędów stwora (<see cref="SettingAttribute"/>: prędkości, zasięg wąsów, częstotliwość zegara…), które różnią się
    /// od ciała budowanego przez rig, trafiają do każdego ciała w próbach — kopia z chwili wywołania. Cele (Id encji)
    /// się nie kopiują: w próbie oko patrzy na cel próby. Gdy nic się nie różni, zwraca rig bez zmian (ten sam obiekt).
    /// </summary>
    public static SeekRig For(Entity aCreature, BrainModule? aModule = null)
    {
        var active = aCreature as ActiveEntity;
        aModule ??= active is null ? null : TrainingController.FindTrainable(active);
        var rig = (aModule is not null ? active?.TrainingRigFor(aModule) : active?.TrainingRig) ?? Generic(aCreature);
        var tuning = SlotTuning(aCreature, rig, aModule);
        if (tuning.Count == 0)
            return rig;
        var create = rig.CreateCreature;
        return rig with
        {
            CreateCreature = (aTargetId, aController) =>
            {
                var creature = create(aTargetId, aController);
                ApplySlotTuning(creature, tuning);
                return creature;
            }
        };
    }

    /// <summary>Ustawienia slotów stwora różne od ciała z rigu (slot → nazwa → wartość); cele pominięte.</summary>
    private static Dictionary<string, Dictionary<string, System.Text.Json.JsonElement>> SlotTuning(Entity aCreature, SeekRig aRig, BrainModule? aModule)
    {
        var tuning = new Dictionary<string, Dictionary<string, System.Text.Json.JsonElement>>();
        if (aCreature is not ActiveEntity active || aModule is not { } module || module.CaptureState() is not { } shape)
            return tuning;
        ActiveEntity reference;
        try
        {
            var controller = shape.CreateTrainable(new float[shape.CountParameters()], module.Name);
            reference = aRig.CreateCreature(Guid.Empty, controller);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or BrainException or NotSupportedException)
        {
            return tuning; // mózg nie pasuje do ciała rigu — powie o tym budowa zadania nauki
        }
        foreach (var (slot, source) in Slots(active))
        {
            if (Slots(reference).FirstOrDefault(aPair => aPair.Slot == slot).Owner is not { } target || target.GetType() != source.GetType())
                continue;
            var mine = Settings.Capture(source);
            var theirs = Settings.Capture(target);
            var differing = mine.Where(aPair => !IsTarget(source, aPair.Key) && (!theirs.TryGetValue(aPair.Key, out var other) ||
                    aPair.Value.GetRawText() != other.GetRawText()))
                .ToDictionary(aPair => aPair.Key, aPair => aPair.Value);
            if (differing.Count > 0)
                tuning[slot] = differing;
        }
        return tuning;
    }

    private static void ApplySlotTuning(ActiveEntity aCreature, Dictionary<string, Dictionary<string, System.Text.Json.JsonElement>> aTuning)
    {
        foreach (var (slot, owner) in Slots(aCreature))
            if (aTuning.TryGetValue(slot, out var values))
                Settings.Apply(owner, values);
    }

    private static IEnumerable<(string Slot, object Owner)> Slots(ActiveEntity aCreature) =>
        aCreature.Body.Sensors.Select(aSensor => (aSensor.Slot, (object)aSensor))
            .Concat(aCreature.Body.Actuators.Select(aActuator => (aActuator.Slot, (object)aActuator)));

    private static bool IsTarget(object aOwner, string aSetting) =>
        Settings.Find(aOwner, aSetting)?.Type == typeof(Guid?);

    /// <summary>
    /// Rig dla stwora bez własnego (<see cref="ActiveEntity.TrainingRig"/>): nowy stwór tego samego rodzaju z rejestru
    /// (<see cref="EntityTypes"/>) i ze sterownikiem na podłodze 60 × 60 m, cel 1.5–6 m, bez przeszkód, wysiłek = średnia
    /// wielkość komend. Encja spoza rejestru — rig walca.
    /// </summary>
    public static SeekRig Generic(Entity aCreature)
    {
        if (EntityTypes.Of(aCreature) is not { IsCreature: true } type)
            return Disk;
        return new SeekRig(
            type.Name.ToLowerInvariant(),
            (aTargetId, aController) => new Spawn(type)
            {
                Slots = Spawn.Aim(aTargetId),
                Brain = Spawn.Controller(aController)
            }.Build<ActiveEntity>(),
            AverageCommand,
            new SeekTargetOptions(),
            AddFloor);
    }
}

using System.Collections.Concurrent;
using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Core.Training;

public sealed record SeekTargetOptions
{
    public int EpisodesPerGeneration { get; init; } = 8;
    public float EpisodeSeconds { get; init; } = 8;
    public float Delta { get; init; } = 1f / 30f;
    public float MinDistance { get; init; } = 1.5f;
    public float MaxDistance { get; init; } = 6;
    public int Seed { get; init; } = 1;

    /// <summary>Koszt energii ruchu: waga · średni wysiłek (patrz <see cref="SeekRig.Effort"/>). Bez niego sieć „taranuje” cel.</summary>
    public float EnergyWeight { get; init; } = 0.1f;

    /// <summary>Liczba cylindrów na drodze do celu w próbie (losowana z zakresu).</summary>
    public int MinObstacles { get; init; }
    public int MaxObstacles { get; init; }
    public float MinObstacleRadius { get; init; } = 0.3f;
    public float MaxObstacleRadius { get; init; } = 0.7f;

    /// <summary>
    /// Kara za kontakt z przeszkodą: waga · ułamek czasu próby w kontakcie. Bez niej sieć uczy się
    /// „przepychać” po cylindrach, bo kolizje i tak ją przesuwają.
    /// </summary>
    public float ContactWeight { get; init; }

    /// <summary>Najmniejsza odległość środka startu od brzegu cylindra — żeby dało się ruszyć i skręcić.</summary>
    public float ObstacleClearanceFromStart { get; init; } = 2;
    public float ObstacleClearanceFromTarget { get; init; } = 1.5f;

    /// <summary>
    /// Stały zestaw prób do wyboru mistrza — niezależny od <see cref="Seed"/>, żeby wyniki mistrzów
    /// z różnych sesji nauki (i etykiety snapshotów) dało się porównywać.
    /// </summary>
    public int ValidationSeed { get; init; } = 1;
    public int ValidationEpisodes { get; init; } = 16;

    /// <summary>Szczelina (powierzchnia–powierzchnia) na końcu próby, przy której cel uznaje się za osiągnięty.</summary>
    public float ReachGap { get; init; } = 0.3f;

    /// <summary>Liczba płaskich klocków w próbie (losowana z zakresu) — stwór uczy się chodzić po nierównym.</summary>
    public int MinSlabs { get; init; }
    public int MaxSlabs { get; init; }
    public float MinSlabSide { get; init; } = 0.8f;
    public float MaxSlabSide { get; init; } = 2.2f;
    public float MinSlabHeight { get; init; } = 0.03f;
    public float MaxSlabHeight { get; init; } = 0.1f;

    /// <summary>
    /// Kara za złą postawę: waga · całka z <see cref="SeekRig.Posture"/> / T (np. pająk leżący brzuchem na ziemi albo
    /// przewrócony). Bez niej ewolucja chętnie „pełza” tułowiem po ziemi, bo i tak dojeżdża do celu.
    /// </summary>
    public float PostureWeight { get; init; }

    /// <summary>Długość ciała za głową w chwili startu (wąż leży wzdłuż −X) — klocki nie mogą leżeć pod nim.</summary>
    public float BodyLength { get; init; }

    /// <summary>
    /// Waga członu dojścia do celu (1 — zwykle; 0 — próba nie jest o dojściu, np. stanie w miejscu, gdzie liczy się tylko
    /// postawa i wysiłek).
    /// </summary>
    public float DistanceWeight { get; init; } = 1;

    /// <summary>
    /// Kara za odejście z miejsca startu: waga · całka z poziomej odległości od startu (m) / T. Dla stania — bez niej
    /// regulator „stoi”, kołysząc się i drobiąc w tył (kilka metrów w 40 s).
    /// </summary>
    public float DriftWeight { get; init; }

    /// <summary>
    /// Pchnięcie w próbie: w chwili <see cref="PushAtSeconds"/> stwór dostaje prędkość o losowym kierunku w poziomie
    /// i wielkości z zakresu (m/s). <see cref="MaxPushSpeed"/> 0 — bez pchnięć.
    /// </summary>
    public float PushAtSeconds { get; init; } = 1.5f;
    public float MinPushSpeed { get; init; }
    public float MaxPushSpeed { get; init; }
}

public readonly record struct ObstacleSpec(Vector2 Position, float Radius);

/// <summary>Klocek terenu: środek spodu, wymiary (X, Y), wysokość, obrót wokół pionu.</summary>
public readonly record struct SlabSpec(Vector2 Position, Vector2 Size, float Height, float Yaw);

/// <summary>
/// Jedna próba: stwór w (0,0) obrócony o Yaw, cel w TargetOffset (na terenie), cylindry i klocki na drodze, opcjonalne
/// pchnięcie (prędkość w poziomie, m/s — patrz <see cref="SeekTargetOptions.MaxPushSpeed"/>).
/// </summary>
public readonly record struct SeekEpisode(float Yaw, Vector2 TargetOffset, ObstacleSpec[] Obstacles, SlabSpec[]? Slabs = null, Vector2? Push = null);

/// <summary>
/// Wynik jednej próby: Cost — składnik fitness (mniejszy = lepszy), FinalGap — szczelina do celu na końcu,
/// ContactTime — sekundy w kontakcie z przeszkodą, Reached — FinalGap ≤ <see cref="SeekTargetOptions.ReachGap"/>.
/// </summary>
public readonly record struct EpisodeResult(float Cost, float FinalGap, float ContactTime, bool Reached, float PostureTime = 0);

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
        (aTargetId, aController) =>
            WorldObjectCatalog.CreateSeeker(Vector3.Zero, Vector3.One, aTargetId, aController),
        aCommand => Math.Clamp(aCommand.GetValueOrDefault(DiskDriveActuator.StepPort), 0, 1)
            + 0.25f * MathF.Min(MathF.Abs(aCommand.GetValueOrDefault(DiskDriveActuator.TurnPort)), 1),
        new SeekTargetOptions(),
        AddFloor);

    private static readonly ConcurrentDictionary<int, SeekRig> CarRigs = new();

    /// <summary>Autko z domyślną liczbą wąsów (<see cref="WorldObjectCatalog.DefaultWhiskers"/>).</summary>
    public static SeekRig Car => CarWith(WorldObjectCatalog.DefaultWhiskers);

    /// <summary>
    /// Autko z <paramref name="aWhiskers"/> wąsami, 1–3 cylindry na drodze, cel 5–9 m, dłuższe próby
    /// (autko nie skręca w miejscu). Ciało w rigu ma tyle wąsów, ile sieć ma wejść Ray{i}.
    /// </summary>
    public static SeekRig CarWith(int aWhiskers)
    {
        WorldObjectCatalog.CheckWhiskerCount(aWhiskers);
        return CarRigs.GetOrAdd(aWhiskers, CreateCarRig);
    }

    private static SeekRig CreateCarRig(int aWhiskers) => new(
        aWhiskers == WorldObjectCatalog.DefaultWhiskers ? "autko" : $"autko ×{aWhiskers}",
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
        (aTargetId, aController) =>
            WorldObjectCatalog.CreateSpider(Vector3.Zero, 0, Vector3.One, aTargetId, aController),
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
        SpiderPosture);

    /// <summary>
    /// Humanoid stoi w miejscu: 10 s, w 1.5 s pchnięcie 0.3–1.0 m/s w losowym kierunku (bez sterowania stoi do ok. 0.4 m/s);
    /// liczy się postawa (upadek, pochylenie), odejście z miejsca i wysiłek — dojście do celu nie (DistanceWeight 0).
    /// </summary>
    public static readonly SeekRig HumanoidStand = new(
        "humanoid · stanie",
        (aTargetId, aController) => WorldObjectCatalog.CreateHumanoid(System.Numerics.Vector3.Zero, 0, aTargetId, aController),
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
        (aTargetId, aController) => WorldObjectCatalog.CreateHumanoid(System.Numerics.Vector3.Zero, 0, aTargetId, aController),
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
        CreateCreature = (aTargetId, aController) => WorldObjectCatalog.CreateMuscleHumanoid(System.Numerics.Vector3.Zero, 0, aTargetId, aController)
    };

    /// <summary>Humanoid mięśniowy idzie do celu: jak <see cref="HumanoidWalk"/> (12 s, cel 2–5 m).</summary>
    public static readonly SeekRig MuscleWalk = HumanoidWalk with
    {
        Name = "humanoid mięśniowy · chód",
        Algorithm = EvolutionAlgorithm.CmaEs,
        CreateCreature = (aTargetId, aController) => WorldObjectCatalog.CreateMuscleHumanoid(System.Numerics.Vector3.Zero, 0, aTargetId, aController)
    };

    /// <summary>
    /// Zła postawa pająka: 1, gdy tułów leży na ziemi albo pająk jest przewrócony (tułów pochylony o 60° i więcej),
    /// pomiędzy — rośnie z przechyleniem.
    /// </summary>
    public static float SpiderPosture(ActiveEntity aCreature)
    {
        if (aCreature is not Creature spider || !WorldObjects.Spider.Design.Is(spider) || spider.PartOrientations.Count == 0)
            return 0;
        if (spider.IsPartTouching(0))
            return 1;
        var up = Vector3.Transform(Vector3.UnitZ, spider.PartOrientations[0]).Z;
        return Math.Clamp((0.95f - up) / 0.45f, 0, 1);
    }

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

/// <summary>
/// Zadanie „dojedź do celu i zatrzymaj się przy nim” dla sieci o podanym stanie (kształt, porty, wyrażenia)
/// w ciele opisanym przez <see cref="SeekRig"/>.
/// Każde pokolenie dostaje nowy zestaw losowych prób, wspólny dla wszystkich kandydatów.
/// Fitness = −średnia z prób [∫ max(Gap, 0) dt / (Gap₀ · T) + EnergyWeight · ∫ wysiłek dt / T
///                             + ContactWeight · czas kontaktu z przeszkodą / T].
/// Pierwszy człon nagradza szybki dojazd i pozostanie przy celu (0 = natychmiast, 1 = brak postępu),
/// drugi karze zbędny ruch (np. wciskanie gazu w kontakcie z celem), trzeci — ocieranie się o przeszkody.
/// Bezpieczne wątkowo: każda ocena buduje własny świat.
/// </summary>
public sealed class SeekTargetTask
{
    private readonly ModuleState _template;
    private readonly string _moduleName;
    private readonly IReadOnlyList<SeekEpisode> _validation;

    /// <param name="aTemplate">
    /// Kształt uczonego modułu: stan sieci (warstwy, porty, wyrażenia) albo CPG (liczba stawów); parametry szablonu są pomijane.
    /// </param>
    public SeekTargetTask(ModuleState aTemplate, SeekRig aRig, SeekTargetOptions? aOptions = null, string aModuleName = "Neural")
    {
        _template = aTemplate;
        _moduleName = aModuleName;
        ParameterCount = aTemplate.CountParameters();
        Rig = aRig;
        Options = aOptions ?? aRig.DefaultOptions;
        _validation = CreateValidationEpisodes(Options);
        // Wczesna walidacja: szablon musi dać poprawny mózg w tym ciele (budowa kompiluje i waliduje graf).
        aRig.CreateCreature(Guid.Empty, CreateModule(new float[ParameterCount]));
    }

    public SeekRig Rig { get; }
    public SeekTargetOptions Options { get; }

    public int ParameterCount { get; }

    public IReadOnlyList<SeekEpisode> EpisodesFor(int aGeneration) => CreateEpisodes(Options, aGeneration);

    /// <summary>Stały zestaw prób walidacyjnych: zależy od ValidationSeed, ValidationEpisodes i parametrów tras, nie od Seed.</summary>
    public IReadOnlyList<SeekEpisode> ValidationEpisodes => _validation;

    public static IReadOnlyList<SeekEpisode> CreateValidationEpisodes(SeekTargetOptions aOptions) =>
        CreateEpisodes(aOptions with
        {
            Seed = unchecked(aOptions.ValidationSeed * 7919 + 17),
            EpisodesPerGeneration = aOptions.ValidationEpisodes
        }, 0);

    public static IReadOnlyList<SeekEpisode> CreateEpisodes(SeekTargetOptions aOptions, int aGeneration)
    {
        var random = new Random(unchecked(aOptions.Seed * 1_000_003 + aGeneration));
        var episodes = new SeekEpisode[aOptions.EpisodesPerGeneration];
        for (var index = 0; index < episodes.Length; index++)
        {
            var angle = random.NextSingle() * MathF.Tau;
            var distance = aOptions.MinDistance + random.NextSingle() * (aOptions.MaxDistance - aOptions.MinDistance);
            var target = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
            var yaw = random.NextSingle() * MathF.Tau;
            var obstacles = PlaceObstacles(aOptions, target, random);
            var slabs = aOptions.MaxSlabs > 0 ? PlaceSlabs(aOptions, target, yaw, random) : null;
            Vector2? push = null;
            if (aOptions.MaxPushSpeed > 0)
            {
                var direction = random.NextSingle() * MathF.Tau;
                var speed = aOptions.MinPushSpeed + random.NextSingle() * (aOptions.MaxPushSpeed - aOptions.MinPushSpeed);
                push = new Vector2(MathF.Cos(direction), MathF.Sin(direction)) * speed;
            }
            episodes[index] = new SeekEpisode(yaw, target, obstacles, slabs, push);
        }
        return episodes;
    }

    /// <summary>
    /// Klocki na drodze start–cel (od ¼ do końca odcinka, z bocznym rozrzutem — cel może leżeć na klocku), nie pod ciałem
    /// stwora na starcie i bez nakładania się środkami.
    /// </summary>
    private static SlabSpec[] PlaceSlabs(SeekTargetOptions aOptions, Vector2 aTarget, float aYaw, Random aRandom)
    {
        var count = aRandom.Next(aOptions.MinSlabs, aOptions.MaxSlabs + 1);
        var slabs = new List<SlabSpec>(count);
        var along = Vector2.Normalize(aTarget);
        var across = new Vector2(-along.Y, along.X);
        var back = -new Vector2(MathF.Cos(aYaw), MathF.Sin(aYaw));
        for (var attempt = 0; slabs.Count < count && attempt < count * 20; attempt++)
        {
            var size = new Vector2(
                aOptions.MinSlabSide + aRandom.NextSingle() * (aOptions.MaxSlabSide - aOptions.MinSlabSide),
                aOptions.MinSlabSide + aRandom.NextSingle() * (aOptions.MaxSlabSide - aOptions.MinSlabSide));
            var height = aOptions.MinSlabHeight + aRandom.NextSingle() * (aOptions.MaxSlabHeight - aOptions.MinSlabHeight);
            var yaw = aRandom.NextSingle() * MathF.PI;
            var position = aTarget * (0.25f + 0.75f * aRandom.NextSingle()) + across * ((aRandom.NextSingle() * 2 - 1) * 0.8f);
            var reach = size.Length() / 2 + 0.3f;
            var underBody = false;
            for (var step = 0f; step <= aOptions.BodyLength + 0.3f && !underBody; step += 0.2f)
                underBody = Vector2.Distance(position, back * step) < reach;
            if (underBody || slabs.Any(aOther => Vector2.Distance(aOther.Position, position) < 0.6f))
                continue;
            slabs.Add(new SlabSpec(position, size, height, yaw));
        }
        return slabs.ToArray();
    }

    /// <summary>Cylindry w środkowej części odcinka start–cel, z bocznym rozrzutem, niezasłaniające startu ani celu.</summary>
    private static ObstacleSpec[] PlaceObstacles(SeekTargetOptions aOptions, Vector2 aTarget, Random aRandom)
    {
        var count = aOptions.MaxObstacles <= 0 ? 0 : aRandom.Next(aOptions.MinObstacles, aOptions.MaxObstacles + 1);
        var obstacles = new List<ObstacleSpec>(count);
        var along = Vector2.Normalize(aTarget);
        var across = new Vector2(-along.Y, along.X);
        for (var attempt = 0; obstacles.Count < count && attempt < count * 20; attempt++)
        {
            var radius = aOptions.MinObstacleRadius + aRandom.NextSingle() * (aOptions.MaxObstacleRadius - aOptions.MinObstacleRadius);
            var position = aTarget * (0.25f + 0.5f * aRandom.NextSingle()) + across * ((aRandom.NextSingle() * 2 - 1) * 0.8f);
            if (position.Length() < radius + aOptions.ObstacleClearanceFromStart ||
                Vector2.Distance(position, aTarget) < radius + aOptions.ObstacleClearanceFromTarget)
                continue;
            if (obstacles.Any(aOther => Vector2.Distance(aOther.Position, position) < aOther.Radius + radius + 0.1f))
                continue;
            obstacles.Add(new ObstacleSpec(position, radius));
        }
        return obstacles.ToArray();
    }

    public float Evaluate(float[] aParameters, int aGeneration) => Evaluate(aParameters, EpisodesFor(aGeneration));

    /// <summary>Ocena na stałym zestawie prób walidacyjnych — porównywalna między pokoleniami i sesjami (do wyboru mistrza).</summary>
    public float Validate(float[] aParameters) => Evaluate(aParameters, _validation);

    public float Evaluate(float[] aParameters, IReadOnlyList<SeekEpisode> aEpisodes) =>
        Evaluate(CreateModule(aParameters), aEpisodes, Options, Rig);

    /// <summary>Fitness dowolnego sterownika (np. heurystyki) w danym ciele na tych samych próbach — do porównań.</summary>
    public static float Evaluate(BrainModule aController, IReadOnlyList<SeekEpisode> aEpisodes, SeekTargetOptions aOptions, SeekRig aRig)
    {
        var results = Run(aController, aEpisodes, aOptions, aRig);
        var total = 0f;
        foreach (var result in results)
            total += result.Cost;
        return -total / results.Count;
    }

    /// <summary>
    /// Przejeżdża próby jedna po drugiej. Każda próba ma własny, świeży świat (podłoga z rigu, cel, cylindry, nowe ciało
    /// z tym samym sterownikiem), a mózg jest resetowany (<see cref="Brain.Reset"/>) — wynik próby nie zależy od tego,
    /// jak skończyła się poprzednia (także przez stan solvera fizyki).
    /// </summary>
    public static IReadOnlyList<EpisodeResult> Run(BrainModule aController, IReadOnlyList<SeekEpisode> aEpisodes,
        SeekTargetOptions aOptions, SeekRig aRig)
    {
        if (aEpisodes.Count == 0)
            throw new ArgumentException("At least one episode is required.", nameof(aEpisodes));

        var ticks = (int)MathF.Ceiling(aOptions.EpisodeSeconds / aOptions.Delta);
        var results = new EpisodeResult[aEpisodes.Count];
        for (var index = 0; index < aEpisodes.Count; index++)
        {
            var episode = aEpisodes[index];
            using var world = new World();
            aRig.PrepareWorld?.Invoke(world);
            foreach (var spec in episode.Slabs ?? [])
                world.Add(WorldObjectCatalog.CreateBox(new Vector3(spec.Position, 0), new Vector3(spec.Size, spec.Height), spec.Yaw));
            var target = WorldObjectCatalog.CreateSphere(new Vector3(episode.TargetOffset, 0));
            world.Add(target);
            Terrain.Snap(world, target);
            var obstacles = new List<Cylinder>();
            foreach (var spec in episode.Obstacles)
            {
                var obstacle = WorldObjectCatalog.CreateCylinder(new Vector3(spec.Position, 0), spec.Radius);
                obstacles.Add(obstacle);
                world.Add(obstacle);
            }

            var creature = aRig.CreateCreature(target.Id, aController);
            if (aRig.Setup is { } setup)
                setup(world, target, creature, episode);
            else
                creature.Place(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, episode.Yaw));
            world.Add(creature);
            var brain = creature.Brain!;
            var drives = brain.Graph.Modules.OfType<ActuatorModule>().ToArray();
            brain.Reset();

            var initialGap = MathF.Max(Gap(creature, target), 1e-3f);
            var distanceCost = 0f;
            var energy = 0f;
            var contact = 0f;
            var posture = 0f;
            var drift = 0f;
            var start = new Vector2(creature.Body.Position.X, creature.Body.Position.Y);
            var pushTick = episode.Push is null ? -1 : (int)MathF.Round(aOptions.PushAtSeconds / aOptions.Delta);
            for (var tick = 0; tick < ticks; tick++)
            {
                if (tick == pushTick && creature is ArticulatedCreature pushed)
                    pushed.Push(new Vector3(episode.Push!.Value, 0));
                world.Update(aOptions.Delta);
                distanceCost += MathF.Max(Gap(creature, target), 0) * aOptions.Delta;
                energy += Effort(aRig, drives) * aOptions.Delta;
                if (obstacles.Count > 0 && Touches(creature, obstacles))
                    contact += aOptions.Delta;
                if (aRig.Posture is { } bad)
                    posture += Math.Clamp(bad(creature), 0, 1) * aOptions.Delta;
                if (aOptions.DriftWeight > 0)
                    drift += Vector2.Distance(new Vector2(creature.Body.Position.X, creature.Body.Position.Y), start) * aOptions.Delta;
            }

            var cost = aOptions.DistanceWeight * distanceCost / (initialGap * aOptions.EpisodeSeconds)
                + aOptions.EnergyWeight * energy / aOptions.EpisodeSeconds
                + aOptions.ContactWeight * contact / aOptions.EpisodeSeconds
                + aOptions.PostureWeight * posture / aOptions.EpisodeSeconds;
            if (aOptions.DriftWeight > 0)
                cost += aOptions.DriftWeight * drift / aOptions.EpisodeSeconds;
            var finalGap = Gap(creature, target);
            results[index] = new EpisodeResult(cost, finalGap, contact, finalGap <= aOptions.ReachGap, posture);
        }
        return results;
    }

    /// <summary>Wysiłek z komend wszystkich napędów (jeden napęd — wprost jego komenda).</summary>
    private static float Effort(SeekRig aRig, ActuatorModule[] aDrives)
    {
        if (aDrives.Length == 1)
            return aRig.Effort(aDrives[0].LastCommand);
        var command = new Dictionary<string, float>();
        foreach (var drive in aDrives)
            foreach (var (port, value) in drive.LastCommand)
                command[drive.Slot + "." + port] = value;
        return aRig.Effort(command);
    }

    private BrainModule CreateModule(float[] aParameters) => _template.CreateTrainable(aParameters, _moduleName);

    /// <summary>
    /// „Kontakt” z przeszkodą: obrys stwora (okrąg <see cref="Entity.BoundingRadius"/>) dotyka cylindra. Celowo obrys, a nie
    /// kontakt w fizyce: autko dostaje karę już z odstępu ~30 cm od boku, co trzyma je z dala od cylindrów. Pomiar
    /// (sieć autka od losowych wag, 20 pok., walidacja 32 tras): obrys 28/32 i 28/32 (ziarna 3 i 5), prawdziwy kontakt
    /// części z cylindrem 2/32 i 25/32 — rzadka kara za faktyczne zderzenie nie uczy omijania.
    /// </summary>
    private static bool Touches(Entity aCreature, List<Cylinder> aObstacles)
    {
        foreach (var obstacle in aObstacles)
            if (Gap(aCreature, obstacle) < 0.01f)
                return true;
        return false;
    }

    private static float Gap(Entity aCreature, Entity aTarget) =>
        Vector3.Distance(aCreature.Body.Position, aTarget.Body.Position) - aCreature.BoundingRadius - aTarget.BoundingRadius;
}

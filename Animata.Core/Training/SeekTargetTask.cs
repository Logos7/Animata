using System.Collections.Concurrent;
using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
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

    /// <summary>Liczba słupków na drodze do celu w próbie (losowana z zakresu).</summary>
    public int MinObstacles { get; init; }
    public int MaxObstacles { get; init; }
    public float MinObstacleRadius { get; init; } = 0.3f;
    public float MaxObstacleRadius { get; init; } = 0.7f;

    /// <summary>
    /// Kara za kontakt z przeszkodą: waga · ułamek czasu próby w kontakcie. Bez niej sieć uczy się
    /// „przepychać” po słupkach, bo kolizje i tak ją przesuwają.
    /// </summary>
    public float ContactWeight { get; init; }

    /// <summary>Najmniejsza odległość środka startu od brzegu słupka — żeby dało się ruszyć i skręcić.</summary>
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

    /// <summary>Liczba płaskich płyt terenu w próbie (losowana z zakresu) — stwór uczy się chodzić po nierównym.</summary>
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

    /// <summary>Długość ciała za głową w chwili startu (wąż leży wzdłuż −X) — płyty nie mogą leżeć pod nim.</summary>
    public float BodyLength { get; init; }
}

public readonly record struct ObstacleSpec(Vector2 Position, float Radius);

/// <summary>Płyta terenu: środek spodu, wymiary (X, Y), wysokość, obrót wokół pionu.</summary>
public readonly record struct SlabSpec(Vector2 Position, Vector2 Size, float Height, float Yaw);

/// <summary>Jedna próba: stwór w (0,0) obrócony o Yaw, cel w TargetOffset (na terenie), słupki i płyty na drodze.</summary>
public readonly record struct SeekEpisode(float Yaw, Vector2 TargetOffset, ObstacleSpec[] Obstacles, SlabSpec[]? Slabs = null);

/// <summary>
/// Wynik jednej próby: Cost — składnik fitness (mniejszy = lepszy), FinalGap — szczelina do celu na końcu,
/// ContactTime — sekundy w kontakcie z przeszkodą, Reached — FinalGap ≤ <see cref="SeekTargetOptions.ReachGap"/>.
/// </summary>
public readonly record struct EpisodeResult(float Cost, float FinalGap, float ContactTime, bool Reached, float PostureTime = 0);

/// <summary>
/// „Ciało do treningu”: jak zbudować stwora z danym sterownikiem i jak liczyć wysiłek z jego komend.
/// <see cref="PrepareWorld"/> dokłada do świata próby to, czego ciało potrzebuje (np. podłogę dla stwora w fizyce).
/// <see cref="Posture"/> — zła postawa w danej chwili (0 = dobra, 1 = zła), karana z wagą <see cref="SeekTargetOptions.PostureWeight"/>.
/// </summary>
public sealed record SeekRig(
    string Name,
    Func<Guid, BrainModule, ActiveEntity> CreateCreature,
    Func<IReadOnlyDictionary<string, float>, float> Effort,
    SeekTargetOptions DefaultOptions,
    Action<World>? PrepareWorld = null,
    Func<ActiveEntity, float>? Posture = null);

public static class SeekRigs
{
    /// <summary>Podłoga prób: wszystkie stwory są bryłami w fizyce, więc muszą na czymś stać.</summary>
    private static void AddFloor(World aWorld) => aWorld.Add(Floor.At(60, 60));

    /// <summary>Walec z napędem różnicowym, bez przeszkód.</summary>
    public static readonly SeekRig Disk = new(
        "walec",
        (aTargetId, aController) =>
            WorldObjectCatalog.CreateSeeker(Vector3.Zero, WorldObjectCatalog.NeuralColor, aTargetId, aController),
        aCommand => Math.Clamp(aCommand.GetValueOrDefault(DiskDriveActuator.StepPort), 0, 1)
            + 0.25f * MathF.Min(MathF.Abs(aCommand.GetValueOrDefault(DiskDriveActuator.TurnPort)), 1),
        new SeekTargetOptions(),
        AddFloor);

    private static readonly ConcurrentDictionary<int, SeekRig> CarRigs = new();

    /// <summary>Autko z domyślną liczbą wąsów (<see cref="WorldObjectCatalog.DefaultWhiskers"/>).</summary>
    public static SeekRig Car => CarWith(WorldObjectCatalog.DefaultWhiskers);

    /// <summary>
    /// Autko z <paramref name="aWhiskers"/> wąsami, 1–3 słupki na drodze, cel 5–9 m, dłuższe próby
    /// (autko nie skręca w miejscu). Ciało w rigu ma tyle wąsów, ile sieć ma wejść Ray{i}.
    /// </summary>
    public static SeekRig CarWith(int aWhiskers)
    {
        if (!WorldObjectCatalog.IsValidWhiskerCount(aWhiskers))
            throw new ArgumentOutOfRangeException(nameof(aWhiskers), aWhiskers,
                $"Autko ma nieparzystą liczbę wąsów od 1 do {WorldObjectCatalog.MaxWhiskers}.");
        return CarRigs.GetOrAdd(aWhiskers, CreateCarRig);
    }

    private static SeekRig CreateCarRig(int aWhiskers) => new(
        aWhiskers == WorldObjectCatalog.DefaultWhiskers ? "autko" : $"autko ×{aWhiskers}",
        (aTargetId, aController) =>
            WorldObjectCatalog.CreateCar(Vector3.Zero, 0, WorldObjectCatalog.NeuralColor, aTargetId, aController, aWhiskers),
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
    /// Wąż z <paramref name="aSegments"/> segmentami w fizyce: podłoga 60 × 60 m, 0–3 płaskie płyty terenu (3–10 cm)
    /// na drodze, bez słupków, cel 3–6 m (także na płycie), 12 s. Wysiłek = średnia wielkość komend stawów.
    /// </summary>
    public static SeekRig SnakeWith(int aSegments)
    {
        if (!WorldObjectCatalog.IsValidSnakeLength(aSegments))
            throw new ArgumentOutOfRangeException(nameof(aSegments), aSegments,
                $"Wąż ma od {WorldObjectCatalog.MinSnakeSegments} do {WorldObjectCatalog.MaxSnakeSegments} segmentów.");
        return SnakeRigs.GetOrAdd(aSegments, CreateSnakeRig);
    }

    private static SeekRig CreateSnakeRig(int aSegments) => new(
        $"wąż ×{aSegments}",
        (aTargetId, aController) =>
            WorldObjectCatalog.CreateSnake(Vector3.Zero, 0, WorldObjectCatalog.NeuralColor, aTargetId, aController, aSegments),
        aCommand =>
        {
            if (aCommand.Count == 0)
                return 0;
            var total = 0f;
            foreach (var value in aCommand.Values)
                total += MathF.Min(MathF.Abs(value), 1);
            return total / aCommand.Count;
        },
        new SeekTargetOptions
        {
            EpisodesPerGeneration = 6,
            EpisodeSeconds = 12,
            MinDistance = 3,
            MaxDistance = 6,
            ValidationEpisodes = 8,
            MaxSlabs = 3,
            BodyLength = aSegments * WorldObjectCatalog.SnakeSpacing
        },
        AddFloor);

    /// <summary>
    /// Pająk (czworonóg) w fizyce: podłoga 60 × 60 m, 0–2 niskie płyty (2–6 cm) na drodze, cel 2–5 m, 12 s.
    /// Wysiłek = średnia wielkość komend stawów.
    /// </summary>
    public static readonly SeekRig Spider = new(
        "pająk",
        (aTargetId, aController) =>
            WorldObjectCatalog.CreateSpider(Vector3.Zero, 0, WorldObjectCatalog.SpiderColor, aTargetId, aController),
        aCommand =>
        {
            if (aCommand.Count == 0)
                return 0;
            var total = 0f;
            foreach (var value in aCommand.Values)
                total += MathF.Min(MathF.Abs(value), 1);
            return total / aCommand.Count;
        },
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
    /// Zła postawa pająka: 1, gdy tułów leży na ziemi albo pająk jest przewrócony (tułów pochylony o 60° i więcej),
    /// pomiędzy — rośnie z przechyleniem.
    /// </summary>
    public static float SpiderPosture(ActiveEntity aCreature)
    {
        if (aCreature is not SpiderCreature spider || spider.PartOrientations.Count == 0)
            return 0;
        if (spider.IsPartTouching(0))
            return 1;
        var up = Vector3.Transform(Vector3.UnitZ, spider.PartOrientations[0]).Z;
        return Math.Clamp((0.95f - up) / 0.45f, 0, 1);
    }

    /// <summary>
    /// Rig pasujący do ciała stwora (autko — z tą samą liczbą wąsów, wąż — z tą samą liczbą segmentów). Jeśli napęd stwora
    /// ma inne ustawienia niż domyślne (prędkość, skręt, moment), ciało w próbach dostaje ich kopię z chwili wywołania.
    /// </summary>
    public static SeekRig For(Entity aCreature)
    {
        var rig = aCreature switch
        {
            SnakeCreature snake => SnakeWith(snake.Segments),
            SpiderCreature => Spider,
            CarCreature car when WorldObjectCatalog.IsValidWhiskerCount(WorldObjectCatalog.WhiskerCountOf(car)) =>
                CarWith(WorldObjectCatalog.WhiskerCountOf(car)),
            CarCreature => Car,
            _ => Disk
        };
        var steering = aCreature.Body.Actuators.OfType<SteeringDriveActuator>().FirstOrDefault() is { } sourceSteering &&
            !SameSettings(sourceSteering, new SteeringDriveActuator())
                ? Copy(sourceSteering)
                : null;
        var disk = aCreature.Body.Actuators.OfType<DiskDriveActuator>().FirstOrDefault() is { } sourceDisk &&
            !SameSettings(sourceDisk, new DiskDriveActuator())
                ? Copy(sourceDisk)
                : null;
        if (steering is null && disk is null)
            return rig;
        var create = rig.CreateCreature;
        return rig with
        {
            CreateCreature = (aTargetId, aController) =>
            {
                var creature = create(aTargetId, aController);
                foreach (var actuator in creature.Body.Actuators)
                    switch (actuator)
                    {
                        case SteeringDriveActuator target when steering is not null:
                            target.CopySettingsFrom(steering);
                            break;
                        case DiskDriveActuator target when disk is not null:
                            target.CopySettingsFrom(disk);
                            break;
                    }
                return creature;
            }
        };
    }

    private static bool SameSettings(SteeringDriveActuator aA, SteeringDriveActuator aB) =>
        aA.MaxSpeed == aB.MaxSpeed && aA.MaxReverseSpeed == aB.MaxReverseSpeed && aA.MaxSteerAngle == aB.MaxSteerAngle &&
        aA.WheelBase == aB.WheelBase && aA.DriveTorque == aB.DriveTorque;

    private static bool SameSettings(DiskDriveActuator aA, DiskDriveActuator aB) =>
        aA.MaxSpeed == aB.MaxSpeed && aA.MaxTurnSpeed == aB.MaxTurnSpeed && aA.DriveTorque == aB.DriveTorque;

    private static SteeringDriveActuator Copy(SteeringDriveActuator aSource)
    {
        var copy = new SteeringDriveActuator();
        copy.CopySettingsFrom(aSource);
        return copy;
    }

    private static DiskDriveActuator Copy(DiskDriveActuator aSource)
    {
        var copy = new DiskDriveActuator();
        copy.CopySettingsFrom(aSource);
        return copy;
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
        ParameterCount = TrainableModules.ParameterCount(aTemplate);
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
            episodes[index] = new SeekEpisode(yaw, target, obstacles, slabs);
        }
        return episodes;
    }

    /// <summary>
    /// Płyty na drodze start–cel (od ¼ do końca odcinka, z bocznym rozrzutem — cel może leżeć na płycie), nie pod ciałem
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

    /// <summary>Słupki w środkowej części odcinka start–cel, z bocznym rozrzutem, niezasłaniające startu ani celu.</summary>
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
    /// Przejeżdża próby jedna po drugiej. Każda próba ma własny, świeży świat (podłoga z rigu, cel, słupki, nowe ciało
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
                world.Add(WorldObjectCatalog.CreateSlab(new Vector3(spec.Position, 0), new Vector3(spec.Size, spec.Height), spec.Yaw));
            var target = WorldObjectCatalog.CreateTargetBall(new Vector3(episode.TargetOffset, 0));
            world.Add(target);
            Terrain.Snap(world, target);
            var obstacles = new List<Obstacle>();
            foreach (var spec in episode.Obstacles)
            {
                var obstacle = WorldObjectCatalog.CreateObstacle(new Vector3(spec.Position, 0), spec.Radius);
                obstacles.Add(obstacle);
                world.Add(obstacle);
            }

            var creature = aRig.CreateCreature(target.Id, aController);
            creature.Place(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, episode.Yaw));
            world.Add(creature);
            var brain = creature.Brain!;
            var wheels = brain.Graph.Modules.OfType<ActuatorModule>().Single();
            brain.Reset();

            var initialGap = MathF.Max(Gap(creature, target), 1e-3f);
            var distanceCost = 0f;
            var energy = 0f;
            var contact = 0f;
            var posture = 0f;
            for (var tick = 0; tick < ticks; tick++)
            {
                world.Update(aOptions.Delta);
                distanceCost += MathF.Max(Gap(creature, target), 0) * aOptions.Delta;
                energy += aRig.Effort(wheels.LastCommand) * aOptions.Delta;
                if (obstacles.Count > 0 && Touches(creature, obstacles))
                    contact += aOptions.Delta;
                if (aRig.Posture is { } bad)
                    posture += Math.Clamp(bad(creature), 0, 1) * aOptions.Delta;
            }

            var cost = distanceCost / (initialGap * aOptions.EpisodeSeconds)
                + aOptions.EnergyWeight * energy / aOptions.EpisodeSeconds
                + aOptions.ContactWeight * contact / aOptions.EpisodeSeconds
                + aOptions.PostureWeight * posture / aOptions.EpisodeSeconds;
            var finalGap = Gap(creature, target);
            results[index] = new EpisodeResult(cost, finalGap, contact, finalGap <= aOptions.ReachGap, posture);
        }
        return results;
    }

    private BrainModule CreateModule(float[] aParameters) => TrainableModules.Create(_template, aParameters, _moduleName);

    private static bool Touches(Entity aCreature, List<Obstacle> aObstacles)
    {
        foreach (var obstacle in aObstacles)
            if (Gap(aCreature, obstacle) < 0.01f)
                return true;
        return false;
    }

    private static float Gap(Entity aCreature, Entity aTarget) =>
        Vector3.Distance(aCreature.Body.Position, aTarget.Body.Position) - aCreature.BoundingRadius - aTarget.BoundingRadius;
}

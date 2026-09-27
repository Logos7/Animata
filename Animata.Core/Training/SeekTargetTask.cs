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
}

public readonly record struct ObstacleSpec(Vector2 Position, float Radius);

/// <summary>Jedna próba: stwór w (0,0) obrócony o Yaw, cel w TargetOffset, słupki na drodze.</summary>
public readonly record struct SeekEpisode(float Yaw, Vector2 TargetOffset, ObstacleSpec[] Obstacles);

/// <summary>
/// Wynik jednej próby: Cost — składnik fitness (mniejszy = lepszy), FinalGap — szczelina do celu na końcu,
/// ContactTime — sekundy w kontakcie z przeszkodą, Reached — FinalGap ≤ <see cref="SeekTargetOptions.ReachGap"/>.
/// </summary>
public readonly record struct EpisodeResult(float Cost, float FinalGap, float ContactTime, bool Reached);

/// <summary>
/// „Ciało do treningu”: jak zbudować stwora z danym sterownikiem i jak liczyć wysiłek z jego komend.
/// </summary>
public sealed record SeekRig(
    string Name,
    Func<Guid, BrainModule, ActiveEntity> CreateCreature,
    Func<IReadOnlyDictionary<string, float>, float> Effort,
    SeekTargetOptions DefaultOptions);

public static class SeekRigs
{
    /// <summary>Walec z napędem różnicowym, bez przeszkód.</summary>
    public static readonly SeekRig Disk = new(
        "walec",
        (aTargetId, aController) =>
            WorldObjectCatalog.CreateSeeker(Vector3.Zero, WorldObjectCatalog.NeuralColor, aTargetId, aController),
        aCommand => Math.Clamp(aCommand.GetValueOrDefault(DiskDriveActuator.StepPort), 0, 1)
            + 0.25f * MathF.Min(MathF.Abs(aCommand.GetValueOrDefault(DiskDriveActuator.TurnPort)), 1),
        new SeekTargetOptions());

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
        });

    /// <summary>Rig pasujący do ciała stwora (autko — z tą samą liczbą wąsów).</summary>
    public static SeekRig For(Entity aCreature)
    {
        if (aCreature is not CarCreature car)
            return Disk;
        var whiskers = WorldObjectCatalog.WhiskerCountOf(car);
        return WorldObjectCatalog.IsValidWhiskerCount(whiskers) ? CarWith(whiskers) : Car;
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
    private readonly NeuralNetworkState _template;
    private readonly IReadOnlyList<SeekEpisode> _validation;

    /// <param name="aTemplate">Kształt sieci i powiązania portów; wagi szablonu są pomijane.</param>
    public SeekTargetTask(NeuralNetworkState aTemplate, SeekRig aRig, SeekTargetOptions? aOptions = null)
    {
        _template = aTemplate;
        Rig = aRig;
        Options = aOptions ?? aRig.DefaultOptions;
        _validation = CreateValidationEpisodes(Options);
        // Wczesna walidacja: szablon musi dać poprawny mózg w tym ciele (budowa kompiluje i waliduje graf).
        aRig.CreateCreature(Guid.Empty, CreateModule(new float[ParameterCount]));
    }

    public SeekRig Rig { get; }
    public SeekTargetOptions Options { get; }

    public int ParameterCount => _template.ParameterCount;

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
            episodes[index] = new SeekEpisode(random.NextSingle() * MathF.Tau, target, PlaceObstacles(aOptions, target, random));
        }
        return episodes;
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
    /// Przejeżdża próby jedna po drugiej jednym stworem. Przed każdą próbą mózg jest resetowany
    /// (<see cref="Brain.Reset"/>), więc wynik próby nie zależy od tego, jak skończyła się poprzednia.
    /// </summary>
    public static IReadOnlyList<EpisodeResult> Run(BrainModule aController, IReadOnlyList<SeekEpisode> aEpisodes,
        SeekTargetOptions aOptions, SeekRig aRig)
    {
        if (aEpisodes.Count == 0)
            throw new ArgumentException("At least one episode is required.", nameof(aEpisodes));

        var world = new World();
        var target = WorldObjectCatalog.CreateTargetBall(Vector3.Zero);
        var creature = aRig.CreateCreature(target.Id, aController);
        world.Add(target);
        world.Add(creature);
        var brain = creature.Brain!;
        var wheels = brain.Graph.Modules.OfType<ActuatorModule>().Single();
        var obstacles = new List<Obstacle>();

        var ticks = (int)MathF.Ceiling(aOptions.EpisodeSeconds / aOptions.Delta);
        var results = new EpisodeResult[aEpisodes.Count];
        for (var index = 0; index < aEpisodes.Count; index++)
        {
            var episode = aEpisodes[index];
            foreach (var obstacle in obstacles)
                world.Remove(obstacle);
            obstacles.Clear();
            foreach (var spec in episode.Obstacles)
            {
                var obstacle = WorldObjectCatalog.CreateObstacle(new Vector3(spec.Position, 0), spec.Radius);
                obstacles.Add(obstacle);
                world.Add(obstacle);
            }

            creature.Body.Position = Vector3.Zero;
            creature.Body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, episode.Yaw);
            target.Body.Position = new Vector3(episode.TargetOffset, 0);
            brain.Reset();

            var initialGap = MathF.Max(Gap(creature, target), 1e-3f);
            var distanceCost = 0f;
            var energy = 0f;
            var contact = 0f;
            for (var tick = 0; tick < ticks; tick++)
            {
                world.Update(aOptions.Delta);
                distanceCost += MathF.Max(Gap(creature, target), 0) * aOptions.Delta;
                energy += aRig.Effort(wheels.LastCommand) * aOptions.Delta;
                if (obstacles.Count > 0 && Touches(creature, obstacles))
                    contact += aOptions.Delta;
            }

            var cost = distanceCost / (initialGap * aOptions.EpisodeSeconds)
                + aOptions.EnergyWeight * energy / aOptions.EpisodeSeconds
                + aOptions.ContactWeight * contact / aOptions.EpisodeSeconds;
            var finalGap = Gap(creature, target);
            results[index] = new EpisodeResult(cost, finalGap, contact, finalGap <= aOptions.ReachGap);
        }
        return results;
    }

    private NeuralNetworkModule CreateModule(float[] aParameters) => NeuralNetworkModule.Create(_template, aParameters);

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

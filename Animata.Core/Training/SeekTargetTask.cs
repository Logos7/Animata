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

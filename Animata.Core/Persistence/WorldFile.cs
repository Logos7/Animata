using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.Entities;
using Animata.Core.Sensors;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Core.Persistence;

// ---------- dokument ----------

/// <summary>
/// Zapisany świat (JSON). Format w <see cref="WorldFile.Format"/> — przy zmianie formatu podbić.
/// Zapisuje się: obiekty (z Id, żeby oko mogło wskazywać inny obiekt) w jednym kształcie dla każdego rodzaju — rodzaj
/// z rejestru (<see cref="EntityTypes"/>), poza, ustawienia (<see cref="SettingAttribute"/>) obiektu, jego zmysłów
/// i napędów (po slotach), pozy części wygiętego stwora i pełny mózg: strukturę (moduły, porty, połączenia, podgrafy,
/// położenia węzłów w edytorze), stan modułów (wagi, parametry) i snapshoty. Nie zapisuje się stanu chwilowego
/// (pamięć sterowników, faza CPG, prędkości w fizyce) ani nauki w toku — po wczytaniu nauka startuje od zapisanych parametrów.
/// </summary>
public sealed record WorldDocument(int Format, string Name, double Time, IReadOnlyList<EntityDocument> Entities);

/// <summary>
/// Obiekt świata — ten sam kształt dla każdego rodzaju. <paramref name="Type"/> — identyfikator z <see cref="EntityTypes"/>;
/// <paramref name="Settings"/> — ustawienia obiektu; <paramref name="Sensors"/> / <paramref name="Actuators"/> — ustawienia
/// zmysłów i napędów po slotach (tylko te, które mają ustawienia); <paramref name="Parts"/> — pozy części (x, y, z, qx, qy, qz, qw
/// na część), gdy stwór nie stoi w pozie spoczynkowej (np. wąż owinięty wokół pnia); <paramref name="Brain"/> — mózg stwora.
/// </summary>
public sealed record EntityDocument(
    Guid Id,
    string Type,
    string Name,
    Vector3 Position,
    Quaternion Rotation,
    IReadOnlyDictionary<string, JsonElement> Settings,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? Sensors = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? Actuators = null,
    float[]? Parts = null,
    BrainDocument? Brain = null);

/// <summary>Mózg: moduły, połączenia, położenia węzłów w edytorze (Id → x, y) i snapshoty.</summary>
public sealed record BrainDocument(
    IReadOnlyList<ModuleDocument> Modules,
    IReadOnlyList<BrainConnection> Connections,
    IReadOnlyDictionary<Guid, float[]> Positions,
    IReadOnlyList<BrainSnapshot> Snapshots,
    Guid? Current = null);

/// <summary>
/// Moduł mózgu w pliku: każdy moduł opisany swoim stanem (<see cref="StateNode"/>), jedynie podgraf ma osobny rekord,
/// bo jego wnętrze to struktura (moduły i połączenia), a nie stan.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(StateNode), "state")]
[JsonDerivedType(typeof(CompositeNode), "composite")]
public abstract record ModuleDocument(Guid Id, string Name);

/// <summary>Moduł opisany w całości swoim stanem (<see cref="ModuleState.CreateModule"/>): sieć, CPG, chód, sterowniki, stała, router.</summary>
public sealed record StateNode(Guid Id, string Name, ModuleState State) : ModuleDocument(Id, Name);

/// <summary>Podgraf: Id granic, porty i wnętrze (bez granic; połączenia wnętrza odwołują się do Id granic).</summary>
public sealed record CompositeNode(Guid Id, string Name, Guid InputId, Guid OutputId, string[] Inputs, string[] Outputs,
    BrainDocument Inner) : ModuleDocument(Id, Name);

// ---------- zapis i odczyt ----------

/// <summary>Zapis i odczyt świata: <see cref="Capture"/> → <see cref="ToJson"/> / <see cref="FromJson"/> → <see cref="Restore"/>.</summary>
public static class WorldFile
{
    /// <summary>
    /// Format 5: każdy obiekt w jednym kształcie (<see cref="EntityDocument"/>: rodzaj, ustawienia, sloty, części, mózg),
    /// każdy moduł mózgu poza podgrafem jako stan (<see cref="StateNode"/>). Formaty 3 i 4 są czytane i przepisywane
    /// (<see cref="WorldFileMigration"/>); starsze nie.
    /// </summary>
    public const int Format = 5;

    /// <summary>Sugerowane rozszerzenie pliku.</summary>
    public const string Extension = ".animata.json";

    /// <summary>Opcje JSON: wektory i kwaterniony jako tablice liczb (jak w ustawieniach, <see cref="Settings.Json"/>).</summary>
    internal static readonly JsonSerializerOptions Options = new(Settings.Json)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string ToJson(WorldDocument aDocument) => JsonSerializer.Serialize(aDocument, Options);

    public static WorldDocument FromJson(string aJson)
    {
        var node = JsonNode.Parse(aJson) as JsonObject ?? throw new JsonException("Pusty plik świata.");
        var format = node["Format"]?.GetValue<int>() ?? 0;
        if (format < WorldFileMigration.Oldest || format > Format)
            throw new NotSupportedException($"Plik świata ma format {format}, a ta wersja czyta formaty {WorldFileMigration.Oldest}–{Format}.");
        node = WorldFileMigration.Migrate(node);
        return node.Deserialize<WorldDocument>(Options) ?? throw new JsonException("Pusty plik świata.");
    }

    // ---------- świat → dokument ----------

    /// <summary>Stan świata do zapisu. Rzuca <see cref="NotSupportedException"/> dla obiektu spoza rejestru albo nieznanego modułu.</summary>
    public static WorldDocument Capture(World aWorld, string aName = "") =>
        new(Format, aName, aWorld.Time, [.. aWorld.Entities.Select(CaptureEntity)]);

    // ---------- schowek: kopie encji ----------

    /// <summary>Encje jako dokumenty (do schowka: kopiuj / wytnij). Rzuca <see cref="NotSupportedException"/> jak <see cref="Capture"/>.</summary>
    public static IReadOnlyList<EntityDocument> CaptureEntities(IEnumerable<Entity> aEntities) =>
        [.. aEntities.Select(CaptureEntity)];

    /// <summary>
    /// Nowe encje z dokumentów (wklej): każda dostaje nowe Id i pozycję przesuniętą o <paramref name="aOffset"/>; mózgi,
    /// snapshoty i ustawienia są kopiami. Ustawienie zmysłu wskazujące encję wklejaną razem z nim (np. cel oka) przechodzi
    /// na jej kopię, inne zostaje (kopia stwora poluje na tę samą kulę co oryginał). Nic nie trafia do świata — to robi wołający.
    /// </summary>
    public static IReadOnlyList<Entity> RestoreCopies(IReadOnlyList<EntityDocument> aDocuments, Vector3 aOffset)
    {
        var ids = aDocuments.ToDictionary(aDocument => aDocument.Id, _ => Guid.NewGuid());
        JsonElement Remap(JsonElement aValue) =>
            aValue.ValueKind == JsonValueKind.String && Guid.TryParse(aValue.GetString(), out var id) && ids.TryGetValue(id, out var copy)
                ? JsonSerializer.SerializeToElement(copy)
                : aValue;
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? RemapSlots(
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? aSlots) =>
            aSlots?.ToDictionary(aSlot => aSlot.Key,
                aSlot => (IReadOnlyDictionary<string, JsonElement>)aSlot.Value.ToDictionary(aValue => aValue.Key, aValue => Remap(aValue.Value)));

        return
        [
            .. aDocuments.Select(aDocument => RestoreEntity(aDocument with
            {
                Id = ids[aDocument.Id],
                Position = aDocument.Position + aOffset,
                Sensors = RemapSlots(aDocument.Sensors),
                Actuators = RemapSlots(aDocument.Actuators)
            }))
        ];
    }

    private static EntityDocument CaptureEntity(Entity aEntity)
    {
        var type = EntityTypes.Of(aEntity) ?? throw new NotSupportedException($"Zapis nie zna encji {aEntity.GetType().Name}.");
        var creature = aEntity as ActiveEntity;
        return new EntityDocument(aEntity.Id, type.Id, aEntity.Name, aEntity.Body.Position, aEntity.Body.Rotation,
            Settings.Capture(aEntity),
            creature is null ? null : SlotSettings(creature.Body.Sensors.Select(aSensor => (aSensor.Slot, (object)aSensor))),
            creature is null ? null : SlotSettings(creature.Body.Actuators.Select(aActuator => (aActuator.Slot, (object)aActuator))),
            aEntity is ArticulatedCreature articulated ? PartsOf(articulated) : null,
            creature?.Brain is { } brain ? CaptureBrain(brain) : null);
    }

    /// <summary>Ustawienia slotów (tylko tych, które je mają) albo null, gdy żaden nie ma.</summary>
    private static Dictionary<string, IReadOnlyDictionary<string, JsonElement>>? SlotSettings(IEnumerable<(string Slot, object Item)> aSlots)
    {
        var slots = new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>();
        foreach (var (slot, item) in aSlots)
            if (Settings.Capture(item) is { Count: > 0 } values)
                slots[slot] = values;
        return slots.Count > 0 ? slots : null;
    }

    /// <summary>Pozy części albo null, gdy stawy kulowe stwora są proste (wystarczy poza korzenia i poza spoczynkowa).</summary>
    private static float[]? PartsOf(ArticulatedCreature aCreature)
    {
        var bent = false;
        for (var joint = 0; joint < aCreature.JointCount && !bent; joint++)
            bent = aCreature.Plan.Joints[joint].Bends &&
                   (MathF.Abs(aCreature.JointYaw(joint)) >= 1e-3f || MathF.Abs(aCreature.JointPitch(joint)) >= 1e-3f);
        if (!bent)
            return null;
        var parts = new float[7 * aCreature.PartPositions.Count];
        for (var part = 0; part < aCreature.PartPositions.Count; part++)
        {
            var position = aCreature.PartPositions[part];
            var orientation = aCreature.PartOrientations[part];
            new[] { position.X, position.Y, position.Z, orientation.X, orientation.Y, orientation.Z, orientation.W }.CopyTo(parts, 7 * part);
        }
        return parts;
    }

    /// <summary>Mózg stwora: graf, snapshoty i wskazanie bieżącego snapshotu (<see cref="Brain.CurrentSnapshot"/>).</summary>
    internal static BrainDocument CaptureBrain(Brain aBrain) =>
        CaptureBrain(aBrain.Graph, aBrain.Snapshots) with { Current = aBrain.CurrentSnapshot()?.Id };

    private static BrainDocument CaptureBrain(BrainGraph aGraph, IReadOnlyList<BrainSnapshot> aSnapshots, IEnumerable<BrainModule>? aSkip = null)
    {
        var skip = aSkip?.ToHashSet() ?? [];
        // Węzły zmysłów i napędów to widok ciała (Brain.SyncBody) — nie zapisuje się ich, tylko połączenia do nich.
        var modules = aGraph.Modules.Where(aModule => !skip.Contains(aModule) && aModule is not SensorModule and not ActuatorModule)
            .Select(CaptureModule).ToList();
        var positions = aGraph.Positions.ToDictionary(aEntry => aEntry.Key, aEntry => new[] { aEntry.Value.X, aEntry.Value.Y });
        return new BrainDocument(modules, [.. aGraph.Connections], positions, [.. aSnapshots]);
    }

    private static ModuleDocument CaptureModule(BrainModule aModule) => aModule switch
    {
        CompositeModule composite => new CompositeNode(composite.Id, composite.Name, composite.Input.Id, composite.Output.Id,
            [.. composite.InputPorts], [.. composite.OutputPorts], CaptureBrain(composite.Inner, [], [composite.Input, composite.Output])),
        _ when aModule.CaptureState() is { } state && state.CreateModule(aModule.Id) is not null =>
            new StateNode(aModule.Id, aModule.Name, state),
        _ => throw new NotSupportedException($"Zapis nie zna modułu {aModule.GetType().Name}.")
    };

    // ---------- dokument → świat ----------

    /// <summary>Nowy świat z dokumentu (także czas symulacji). Stwory dostają zapisane mózgi (strukturę, stan i snapshoty).</summary>
    public static DemoScene Restore(WorldDocument aDocument)
    {
        var world = new World { Time = aDocument.Time };
        var creatures = new List<ActiveEntity>();
        try
        {
            foreach (var document in aDocument.Entities)
            {
                var entity = RestoreEntity(document);
                world.Add(entity);
                if (entity is ActiveEntity creature)
                    creatures.Add(creature);
            }
        }
        catch
        {
            world.Dispose();
            throw;
        }
        return new DemoScene(world, creatures);
    }

    /// <summary>Obiekt z dokumentu — ta sama droga co każdy nowy obiekt (<see cref="Spawn"/>), kroki z pliku.</summary>
    private static Entity RestoreEntity(EntityDocument aDocument)
    {
        var type = EntityTypes.Find(aDocument.Type) ?? throw new NotSupportedException($"Nieznany rodzaj obiektu w pliku: {aDocument.Type}.");
        var position = aDocument.Position;
        var rotation = NormalizedIfNeeded(aDocument.Rotation);
        return new Spawn(type)
        {
            Settings = aEntity => Settings.Apply(aEntity, aDocument.Settings),
            Slots = aCreature =>
            {
                foreach (var (slot, values) in aDocument.Sensors ?? Empty)
                    if (aCreature.Body.FindSensor(slot) is { } sensor)
                        Settings.Apply(sensor, values);
                foreach (var (slot, values) in aDocument.Actuators ?? Empty)
                    if (aCreature.Body.FindActuator(slot) is { } actuator)
                        Settings.Apply(actuator, values);
            },
            Brain = aCreature =>
            {
                if (aDocument.Brain is { } brain && aCreature.Brain is { } target)
                    RestoreBrain(target, brain);
            },
            Id = aDocument.Id,
            Name = aDocument.Name,
            Pose = aEntity =>
            {
                if (aDocument.Parts is { } parts && aEntity is ArticulatedCreature bent && parts.Length == 7 * bent.PartPositions.Count)
                {
                    var count = bent.PartPositions.Count;
                    bent.PlaceParts(
                        [.. Enumerable.Range(0, count).Select(aPart => new Vector3(parts[7 * aPart], parts[7 * aPart + 1], parts[7 * aPart + 2]))],
                        [.. Enumerable.Range(0, count).Select(aPart => new Quaternion(parts[7 * aPart + 3], parts[7 * aPart + 4], parts[7 * aPart + 5], parts[7 * aPart + 6]))]);
                }
                else
                    Spawn.At(position, rotation)(aEntity);
            }
        }.Build();
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>> Empty =
        new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>();

    /// <summary>
    /// Zastępuje graf mózgu zapisanym (moduły, połączenia, położenia), dokłada zapisane snapshoty i przywraca bieżący.
    /// </summary>
    internal static void RestoreBrain(Brain aBrain, BrainDocument aDocument)
    {
        FillGraph(aBrain.Graph, aDocument);
        aBrain.SyncBody();
        foreach (var snapshot in aBrain.Snapshots.ToArray())
            aBrain.RemoveSnapshot(snapshot);
        foreach (var snapshot in aDocument.Snapshots)
            aBrain.AddSnapshot(snapshot);
        // Stwór startuje ze wskazanego snapshotu (stan modułów w pliku powinien być z nim zgodny — snapshot rozstrzyga).
        if (aDocument.Current is { } current && aBrain.Snapshots.FirstOrDefault(aSnapshot => aSnapshot.Id == current) is { } start)
            aBrain.Restore(start);
        aBrain.Graph.InvalidateDeep();
        aBrain.Graph.Validate();
    }

    private static void FillGraph(BrainGraph aGraph, BrainDocument aDocument, BrainModule? aKeepInput = null, BrainModule? aKeepOutput = null)
    {
        aGraph.Modules.Clear();
        aGraph.Connections.Clear();
        aGraph.Positions.Clear();
        if (aKeepInput is not null)
            aGraph.Modules.Add(aKeepInput);
        if (aKeepOutput is not null)
            aGraph.Modules.Add(aKeepOutput);
        foreach (var module in aDocument.Modules)
            aGraph.Modules.Add(RestoreModule(module));
        aGraph.Connections.AddRange(aDocument.Connections);
        foreach (var (id, point) in aDocument.Positions)
            if (point.Length >= 2)
                aGraph.Positions[id] = new Vector2(point[0], point[1]);
        aGraph.Invalidate();
    }

    private static BrainModule RestoreModule(ModuleDocument aDocument)
    {
        BrainModule module = aDocument switch
        {
            CompositeNode composite => RestoreComposite(composite),
            StateNode state => RestoreStateModule(state),
            _ => throw new NotSupportedException($"Nieznany rodzaj modułu w pliku: {aDocument.GetType().Name}.")
        };
        module.Name = aDocument.Name;
        return module;
    }

    private static CompositeModule RestoreComposite(CompositeNode aDocument)
    {
        var composite = new CompositeModule(aDocument.InputId, aDocument.OutputId, aDocument.Inputs, aDocument.Outputs) { Id = aDocument.Id };
        FillGraph(composite.Inner, aDocument.Inner, composite.Input, composite.Output);
        return composite;
    }

    private static BrainModule RestoreStateModule(StateNode aDocument)
    {
        var module = aDocument.State.CreateModule(aDocument.Id)
            ?? throw new NotSupportedException($"Nieznany stan modułu w pliku: {aDocument.State.GetType().Name}.");
        module.RestoreState(aDocument.State);
        return module;
    }

    /// <summary>Kwaternion z pliku; normalizowany tylko wtedy, gdy wyraźnie nie jest jednostkowy (zapis i odczyt są wtedy bit w bit).</summary>
    private static Quaternion NormalizedIfNeeded(Quaternion aRotation) =>
        MathF.Abs(aRotation.LengthSquared() - 1) > 1e-3f ? Quaternion.Normalize(aRotation) : aRotation;
}

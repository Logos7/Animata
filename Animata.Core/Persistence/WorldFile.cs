using System.Numerics;
using System.Text.Json;
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
/// Zapisuje się: encje (z Id, żeby oko mogło wskazywać kulę), ich pozy i wymiary, parametry ciał (liczba wąsów,
/// segmentów), cel oka i pełny mózg: strukturę (moduły, porty, połączenia, podgrafy, położenia węzłów w edytorze),
/// stan modułów (wagi, parametry) i snapshoty. Nie zapisuje się stanu chwilowego (pamięć sterowników, faza CPG,
/// prędkości w fizyce) ani nauki w toku — po wczytaniu nauka startuje od zapisanych parametrów.
/// </summary>
public sealed record WorldDocument(int Format, string Name, double Time, IReadOnlyList<EntityDocument> Entities);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(BoxDocument), "box")]
[JsonDerivedType(typeof(CylinderDocument), "cylinder")]
[JsonDerivedType(typeof(SphereDocument), "sphere")]
[JsonDerivedType(typeof(CarDocument), "car")]
[JsonDerivedType(typeof(CylinderCreatureDocument), "cylinderCreature")]
[JsonDerivedType(typeof(SnakeDocument), "snake")]
[JsonDerivedType(typeof(SpiderDocument), "spider")]
public abstract record EntityDocument(Guid Id, string Name, float[] Position, float[] Rotation);

/// <summary>Klocek: środek spodu, obrót, wymiary (X, Y, wysokość), kolor, blokada (podłoga).</summary>
public sealed record BoxDocument(Guid Id, string Name, float[] Position, float[] Rotation, float[] Size, float[] Color, bool Locked)
    : EntityDocument(Id, Name, Position, Rotation);

/// <summary>Cylinder: podstawa, promień, wysokość, tarcie chwytne (0 — brak), kolor, blokada.</summary>
public sealed record CylinderDocument(Guid Id, string Name, float[] Position, float[] Rotation, float Radius, float Height, float Grip,
    float[] Color, bool Locked) : EntityDocument(Id, Name, Position, Rotation);

/// <summary>Kula (cel oka): spód, promień, blokada.</summary>
public sealed record SphereDocument(Guid Id, string Name, float[] Position, float[] Rotation, float Radius, bool Locked)
    : EntityDocument(Id, Name, Position, Rotation);

public sealed record CarDocument(Guid Id, string Name, float[] Position, float[] Rotation, float[] Color, int Whiskers,
    float WhiskerRange, Guid? Target, BrainDocument Brain, DriveDocument? Drive = null) : EntityDocument(Id, Name, Position, Rotation);

public sealed record CylinderCreatureDocument(Guid Id, string Name, float[] Position, float[] Rotation, float[] Color, Guid? Target,
    BrainDocument Brain, DriveDocument? Drive = null) : EntityDocument(Id, Name, Position, Rotation);

/// <summary>Ustawienia napędu (autko: prędkości, skręt, moment; walec: prędkość, obrót, moment). Brak = domyślne.</summary>
public sealed record DriveDocument(float MaxSpeed, float MaxReverseSpeed, float MaxSteerAngle, float MaxTurnSpeed, float DriveTorque);

/// <summary>
/// Wąż; Parts — pozy części (x, y, z, qx, qy, qz, qw na część), gdy wąż nie leży prosto (np. owinięty wokół pnia),
/// żeby wczytał się w tej samej pozie.
/// </summary>
public sealed record SnakeDocument(Guid Id, string Name, float[] Position, float[] Rotation, float[] Color, int Segments,
    Guid? Target, BrainDocument Brain, bool Climber = false, float[]? Parts = null) : EntityDocument(Id, Name, Position, Rotation);

public sealed record SpiderDocument(Guid Id, string Name, float[] Position, float[] Rotation, float[] Color,
    Guid? Target, BrainDocument Brain) : EntityDocument(Id, Name, Position, Rotation);

/// <summary>Mózg: moduły, połączenia, położenia węzłów w edytorze (Id → x, y) i snapshoty.</summary>
public sealed record BrainDocument(
    IReadOnlyList<ModuleDocument> Modules,
    IReadOnlyList<BrainConnection> Connections,
    IReadOnlyDictionary<Guid, float[]> Positions,
    IReadOnlyList<BrainSnapshot> Snapshots,
    Guid? Current = null);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(StateNode), "state")]
[JsonDerivedType(typeof(ConstantNode), "constant")]
[JsonDerivedType(typeof(RouterNode), "router")]
[JsonDerivedType(typeof(CompositeNode), "composite")]
public abstract record ModuleDocument(Guid Id, string Name);

/// <summary>Moduł opisany w całości swoim stanem: sieć, CPG, AvoidAndSeek, ApproachTarget.</summary>
public sealed record StateNode(Guid Id, string Name, ModuleState State) : ModuleDocument(Id, Name);

public sealed record ConstantNode(Guid Id, string Name, string Port, float Value) : ModuleDocument(Id, Name);

public sealed record RouterNode(Guid Id, string Name, int Channels, string[] Ports) : ModuleDocument(Id, Name);

/// <summary>Podgraf: Id granic, porty i wnętrze (bez granic; połączenia wnętrza odwołują się do Id granic).</summary>
public sealed record CompositeNode(Guid Id, string Name, Guid InputId, Guid OutputId, string[] Inputs, string[] Outputs,
    BrainDocument Inner) : ModuleDocument(Id, Name);

// ---------- zapis i odczyt ----------

/// <summary>Zapis i odczyt świata: <see cref="Capture"/> → <see cref="ToJson"/> / <see cref="FromJson"/> → <see cref="Restore"/>.</summary>
public static class WorldFile
{
    /// <summary>
    /// Format 3: bryły geometryczne (box, cylinder, sphere); w mózgu bez węzłów zmysłów i napędów (to widok ciała).
    /// Starsze formaty nie są czytane.
    /// </summary>
    public const int Format = 3;

    /// <summary>Sugerowane rozszerzenie pliku.</summary>
    public const string Extension = ".animata.json";

    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string ToJson(WorldDocument aDocument) => JsonSerializer.Serialize(aDocument, Options);

    public static WorldDocument FromJson(string aJson)
    {
        var document = JsonSerializer.Deserialize<WorldDocument>(aJson, Options) ?? throw new JsonException("Pusty plik świata.");
        if (document.Format != Format)
            throw new NotSupportedException($"Plik świata ma format {document.Format}, a ta wersja czyta tylko format {Format}.");
        return document;
    }

    // ---------- świat → dokument ----------

    /// <summary>Stan świata do zapisu. Rzuca <see cref="NotSupportedException"/> dla encji albo modułu, którego format nie zna.</summary>
    public static WorldDocument Capture(World aWorld, string aName = "") =>
        new(Format, aName, aWorld.Time, [.. aWorld.Entities.Select(CaptureEntity)]);

    // ---------- schowek: kopie encji ----------

    /// <summary>Encje jako dokumenty (do schowka: kopiuj / wytnij). Rzuca <see cref="NotSupportedException"/> jak <see cref="Capture"/>.</summary>
    public static IReadOnlyList<EntityDocument> CaptureEntities(IEnumerable<Entity> aEntities) =>
        [.. aEntities.Select(CaptureEntity)];

    /// <summary>
    /// Nowe encje z dokumentów (wklej): każda dostaje nowe Id i pozycję przesuniętą o <paramref name="aOffset"/>; mózgi,
    /// snapshoty i ustawienia są kopiami. Cel oka wskazujący na encję wklejaną razem z nim przechodzi na jej kopię,
    /// inny cel zostaje (kopia stwora poluje na tę samą kulkę co oryginał). Nic nie trafia do świata — to robi wołający.
    /// </summary>
    public static IReadOnlyList<Entity> RestoreCopies(IReadOnlyList<EntityDocument> aDocuments, Vector3 aOffset)
    {
        var ids = aDocuments.ToDictionary(aDocument => aDocument.Id, _ => Guid.NewGuid());
        Guid? Remap(Guid? aTarget) => aTarget is { } target && ids.TryGetValue(target, out var copy) ? copy : aTarget;

        var copies = new List<Entity>(aDocuments.Count);
        foreach (var document in aDocuments)
        {
            var position = ToVector(document.Position) + aOffset;
            EntityDocument moved = document switch
            {
                CarDocument car => car with { Target = Remap(car.Target) },
                CylinderCreatureDocument cylinder => cylinder with { Target = Remap(cylinder.Target) },
                SnakeDocument snake => snake with { Target = Remap(snake.Target) },
                SpiderDocument spider => spider with { Target = Remap(spider.Target) },
                _ => document
            };
            copies.Add(RestoreEntity(moved with { Id = ids[document.Id], Position = Vector(position) }));
        }
        return copies;
    }

    private static EntityDocument CaptureEntity(Entity aEntity)
    {
        var position = Vector(aEntity.Body.Position);
        var rotation = Rotation(aEntity.Body.Rotation);
        return aEntity switch
        {
            Box box => new BoxDocument(box.Id, box.Name, position, rotation, Vector(box.Size), Vector(box.Color), box.Locked),
            Cylinder cylinder => new CylinderDocument(cylinder.Id, cylinder.Name, position, rotation, cylinder.Radius, cylinder.Height,
                cylinder.Grip, Vector(cylinder.Color), cylinder.Locked),
            Sphere sphere => new SphereDocument(sphere.Id, sphere.Name, position, rotation, sphere.Radius, sphere.Locked),
            CarCreature car => new CarDocument(car.Id, car.Name, position, rotation, Vector(car.Color),
                WorldObjectCatalog.WhiskerCountOf(car), car.Body.Sensors.OfType<RaySensor>().FirstOrDefault()?.Range ?? WorldObjectCatalog.WhiskerRange,
                TargetOf(car), CaptureBrain(car.Brain!), DriveOf(car)),
            CylinderCreature cylinder => new CylinderCreatureDocument(cylinder.Id, cylinder.Name, position, rotation, Vector(cylinder.Color),
                TargetOf(cylinder), CaptureBrain(cylinder.Brain!), DriveOf(cylinder)),
            SnakeCreature snake => new SnakeDocument(snake.Id, snake.Name, position, rotation, Vector(snake.Color), snake.Segments,
                TargetOf(snake), CaptureBrain(snake.Brain!), snake.Climber, PartsOf(snake)),
            SpiderCreature spider => new SpiderDocument(spider.Id, spider.Name, position, rotation, Vector(spider.Color),
                TargetOf(spider), CaptureBrain(spider.Brain!)),
            _ => throw new NotSupportedException($"Zapis nie zna encji {aEntity.GetType().Name}.")
        };
    }

    /// <summary>Pozy części węża albo null, gdy leży prosto (wystarczy poza korzenia).</summary>
    private static float[]? PartsOf(SnakeCreature aSnake)
    {
        var straight = true;
        for (var joint = 0; joint < aSnake.JointCount && straight; joint++)
            straight = MathF.Abs(aSnake.JointYaw(joint)) < 1e-3f && MathF.Abs(aSnake.JointPitch(joint)) < 1e-3f;
        if (straight)
            return null;
        var parts = new float[7 * aSnake.PartPositions.Count];
        for (var part = 0; part < aSnake.PartPositions.Count; part++)
        {
            var position = aSnake.PartPositions[part];
            var orientation = aSnake.PartOrientations[part];
            new[] { position.X, position.Y, position.Z, orientation.X, orientation.Y, orientation.Z, orientation.W }.CopyTo(parts, 7 * part);
        }
        return parts;
    }

    private static DriveDocument? DriveOf(Entity aCreature) => aCreature.Body.Actuators.FirstOrDefault() switch
    {
        Actuators.SteeringDriveActuator steering => new DriveDocument(steering.MaxSpeed, steering.MaxReverseSpeed, steering.MaxSteerAngle, 0, steering.DriveTorque),
        Actuators.DiskDriveActuator disk => new DriveDocument(disk.MaxSpeed, 0, 0, disk.MaxTurnSpeed, disk.DriveTorque),
        _ => null
    };

    private static void ApplyDrive(Entity aCreature, DriveDocument? aDrive)
    {
        if (aDrive is null)
            return;
        foreach (var actuator in aCreature.Body.Actuators)
            switch (actuator)
            {
                case Actuators.SteeringDriveActuator steering:
                    steering.MaxSpeed = aDrive.MaxSpeed;
                    steering.MaxReverseSpeed = aDrive.MaxReverseSpeed;
                    steering.MaxSteerAngle = aDrive.MaxSteerAngle;
                    steering.DriveTorque = aDrive.DriveTorque;
                    break;
                case Actuators.DiskDriveActuator disk:
                    disk.MaxSpeed = aDrive.MaxSpeed;
                    disk.MaxTurnSpeed = aDrive.MaxTurnSpeed;
                    disk.DriveTorque = aDrive.DriveTorque;
                    break;
            }
    }

    private static Guid? TargetOf(Entity aCreature) => aCreature.Body.Sensors.OfType<TargetSensor>().FirstOrDefault()?.TargetId;

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
        ConstantModule constant => new ConstantNode(constant.Id, constant.Name, constant.Port, constant.Value),
        RouterModule router => new RouterNode(router.Id, router.Name, router.Channels, [.. router.Ports]),
        CompositeModule composite => new CompositeNode(composite.Id, composite.Name, composite.Input.Id, composite.Output.Id,
            [.. composite.InputPorts], [.. composite.OutputPorts], CaptureBrain(composite.Inner, [], [composite.Input, composite.Output])),
        NeuralNetworkModule or CpgModule or GaitModule or AvoidAndSeekModule or ApproachTargetModule =>
            new StateNode(aModule.Id, aModule.Name, aModule.CaptureState()!),
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

    private static Entity RestoreEntity(EntityDocument aDocument)
    {
        var position = ToVector(aDocument.Position);
        var rotation = ToRotation(aDocument.Rotation);
        Entity entity;
        switch (aDocument)
        {
            case BoxDocument box:
                entity = new Box
                {
                    Id = box.Id, Size = ToVector(box.Size), Color = ToVector(box.Color), Locked = box.Locked,
                    Body = { Position = position, Rotation = rotation }
                };
                break;
            case CylinderDocument cylinder:
                entity = new Cylinder
                {
                    Id = cylinder.Id, Radius = cylinder.Radius, Height = cylinder.Height, Grip = cylinder.Grip, Color = ToVector(cylinder.Color),
                    Locked = cylinder.Locked, Body = { Position = position, Rotation = rotation }
                };
                break;
            case SphereDocument sphere:
                entity = new Sphere { Id = sphere.Id, Radius = sphere.Radius, Locked = sphere.Locked, Body = { Position = position, Rotation = rotation } };
                break;
            case CarDocument car:
            {
                var creature = WorldObjectCatalog.CreateCar(position, 0, ToVector(car.Color), car.Target,
                    WorldObjectCatalog.CreateAvoidController(car.Whiskers), car.Whiskers);
                if (creature.Body.Sensors.OfType<RaySensor>().FirstOrDefault() is { } whiskers)
                    whiskers.Range = car.WhiskerRange;
                RestoreBrain(creature.Brain!, car.Brain);
                ApplyDrive(creature, car.Drive);
                entity = creature;
                break;
            }
            case CylinderCreatureDocument cylinder:
            {
                var creature = WorldObjectCatalog.CreateSeeker(position, ToVector(cylinder.Color), cylinder.Target, new ApproachTargetModule());
                RestoreBrain(creature.Brain!, cylinder.Brain);
                ApplyDrive(creature, cylinder.Drive);
                entity = creature;
                break;
            }
            case SnakeDocument snake:
            {
                var creature = WorldObjectCatalog.CreateSnake(position, 0, ToVector(snake.Color), snake.Target,
                    WorldObjectCatalog.CreateCpg(snake.Segments), snake.Segments);
                RestoreBrain(creature.Brain!, snake.Brain);
                creature.Climber = snake.Climber;
                entity = creature;
                break;
            }
            case SpiderDocument spider:
            {
                var creature = WorldObjectCatalog.CreateSpider(position, 0, ToVector(spider.Color), spider.Target, new GaitModule());
                RestoreBrain(creature.Brain!, spider.Brain);
                entity = creature;
                break;
            }
            default:
                throw new NotSupportedException($"Nieznany rodzaj encji w pliku: {aDocument.GetType().Name}.");
        }

        entity.AssignId(aDocument.Id);
        entity.Name = aDocument.Name;
        if (aDocument is SnakeDocument { Parts: { } parts } && entity is SnakeCreature bent && parts.Length == 7 * bent.PartPositions.Count)
        {
            var count = bent.PartPositions.Count;
            bent.PlaceParts(
                [.. Enumerable.Range(0, count).Select(aPart => new Vector3(parts[7 * aPart], parts[7 * aPart + 1], parts[7 * aPart + 2]))],
                [.. Enumerable.Range(0, count).Select(aPart => new Quaternion(parts[7 * aPart + 3], parts[7 * aPart + 4], parts[7 * aPart + 5], parts[7 * aPart + 6]))]);
        }
        else if (!entity.Locked)
            entity.Place(position, rotation);
        return entity;
    }

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
            ConstantNode constant => new ConstantModule(constant.Port, constant.Value) { Id = constant.Id },
            RouterNode router => new RouterModule(router.Channels, router.Ports) { Id = router.Id },
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
        BrainModule module = aDocument.State switch
        {
            NeuralNetworkState network => new NeuralNetworkModule(new NeuralNetwork([.. network.Layers])) { Id = aDocument.Id },
            CpgState cpg => new CpgModule(cpg.Joints) { Id = aDocument.Id },
            GaitState => new GaitModule { Id = aDocument.Id },
            AvoidAndSeekState avoid => new AvoidAndSeekModule(avoid.RayAngles) { Id = aDocument.Id },
            ApproachTargetState => new ApproachTargetModule { Id = aDocument.Id },
            _ => throw new NotSupportedException($"Nieznany stan modułu w pliku: {aDocument.State.GetType().Name}.")
        };
        module.RestoreState(aDocument.State);
        return module;
    }

    // ---------- liczby ----------

    private static float[] Vector(Vector3 aVector) => [aVector.X, aVector.Y, aVector.Z];

    private static float[] Rotation(Quaternion aRotation) => [aRotation.X, aRotation.Y, aRotation.Z, aRotation.W];

    private static Vector3 ToVector(float[] aValues) =>
        aValues.Length >= 3 ? new Vector3(aValues[0], aValues[1], aValues[2]) : throw new JsonException("Wektor musi mieć 3 liczby.");

    /// <summary>Kwaternion z pliku; normalizowany tylko wtedy, gdy wyraźnie nie jest jednostkowy (zapis i odczyt są wtedy bit w bit).</summary>
    private static Quaternion ToRotation(float[] aValues)
    {
        if (aValues.Length < 4)
            return Quaternion.Identity;
        var rotation = new Quaternion(aValues[0], aValues[1], aValues[2], aValues[3]);
        return MathF.Abs(rotation.LengthSquared() - 1) > 1e-3f ? Quaternion.Normalize(rotation) : rotation;
    }
}

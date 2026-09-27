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
/// Zapisany świat (JSON). Format w <see cref="Format"/> — przy zmianie formatu podbić i dopisać migrację w <see cref="WorldFile"/>.
/// Zapisuje się: encje (z Id, żeby oko mogło wskazywać kulkę), ich pozy i wymiary, parametry ciał (liczba wąsów,
/// segmentów), cel oka i pełny mózg: strukturę (moduły, porty, połączenia, podgrafy, położenia węzłów w edytorze),
/// stan modułów (wagi, parametry) i snapshoty. Nie zapisuje się stanu chwilowego (pamięć sterowników, faza CPG,
/// prędkości w fizyce) ani nauki w toku — po wczytaniu nauka startuje od zapisanych parametrów.
/// </summary>
public sealed record WorldDocument(int Format, string Name, double Time, IReadOnlyList<EntityDocument> Entities);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(FloorDocument), "floor")]
[JsonDerivedType(typeof(TargetDocument), "target")]
[JsonDerivedType(typeof(ObstacleDocument), "obstacle")]
[JsonDerivedType(typeof(CarDocument), "car")]
[JsonDerivedType(typeof(CylinderDocument), "cylinder")]
[JsonDerivedType(typeof(SnakeDocument), "snake")]
public abstract record EntityDocument(Guid Id, string Name, float[] Position, float[] Rotation);

public sealed record FloorDocument(Guid Id, string Name, float[] Position, float[] Rotation, float[] Size)
    : EntityDocument(Id, Name, Position, Rotation);

public sealed record TargetDocument(Guid Id, string Name, float[] Position, float[] Rotation, float Radius)
    : EntityDocument(Id, Name, Position, Rotation);

public sealed record ObstacleDocument(Guid Id, string Name, float[] Position, float[] Rotation, float Radius, float Height)
    : EntityDocument(Id, Name, Position, Rotation);

public sealed record CarDocument(Guid Id, string Name, float[] Position, float[] Rotation, float[] Color, int Whiskers,
    float WhiskerRange, Guid? Target, BrainDocument Brain, DriveDocument? Drive = null) : EntityDocument(Id, Name, Position, Rotation);

public sealed record CylinderDocument(Guid Id, string Name, float[] Position, float[] Rotation, float[] Color, Guid? Target,
    BrainDocument Brain, DriveDocument? Drive = null) : EntityDocument(Id, Name, Position, Rotation);

/// <summary>Ustawienia napędu (autko: prędkości, skręt, moment; walec: prędkość, obrót, moment). Brak = domyślne.</summary>
public sealed record DriveDocument(float MaxSpeed, float MaxReverseSpeed, float MaxSteerAngle, float MaxTurnSpeed, float DriveTorque);

public sealed record SnakeDocument(Guid Id, string Name, float[] Position, float[] Rotation, float[] Color, int Segments,
    Guid? Target, BrainDocument Brain) : EntityDocument(Id, Name, Position, Rotation);

/// <summary>Mózg: moduły, połączenia, położenia węzłów w edytorze (Id → x, y) i snapshoty.</summary>
public sealed record BrainDocument(
    IReadOnlyList<ModuleDocument> Modules,
    IReadOnlyList<BrainConnection> Connections,
    IReadOnlyDictionary<Guid, float[]> Positions,
    IReadOnlyList<BrainSnapshot> Snapshots,
    Guid? Current = null);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(SensorNode), "sensor")]
[JsonDerivedType(typeof(ActuatorNode), "actuator")]
[JsonDerivedType(typeof(StateNode), "state")]
[JsonDerivedType(typeof(ConstantNode), "constant")]
[JsonDerivedType(typeof(RouterNode), "router")]
[JsonDerivedType(typeof(CompositeNode), "composite")]
public abstract record ModuleDocument(Guid Id, string Name);

/// <summary>Węzeł sensora: slot w ciele i porty.</summary>
public sealed record SensorNode(Guid Id, string Name, string Slot, string[] Ports) : ModuleDocument(Id, Name);

public sealed record ActuatorNode(Guid Id, string Name, string Slot, string[] Ports) : ModuleDocument(Id, Name);

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
    public const int Format = 1;

    /// <summary>Sugerowane rozszerzenie pliku.</summary>
    public const string Extension = ".animata.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string ToJson(WorldDocument aDocument) => JsonSerializer.Serialize(aDocument, Options);

    public static WorldDocument FromJson(string aJson)
    {
        var document = JsonSerializer.Deserialize<WorldDocument>(aJson, Options) ?? throw new JsonException("Pusty plik świata.");
        if (document.Format > Format)
            throw new NotSupportedException($"Plik świata ma format {document.Format}, a ta wersja zna najwyżej {Format}.");
        return document;
    }

    public static void Save(World aWorld, string aPath, string aName = "", double aTime = 0) =>
        File.WriteAllText(aPath, ToJson(Capture(aWorld, aName, aTime)));

    public static WorldDocument Load(string aPath) => FromJson(File.ReadAllText(aPath));

    // ---------- świat → dokument ----------

    /// <summary>Stan świata do zapisu. Rzuca <see cref="NotSupportedException"/> dla encji albo modułu, którego format nie zna.</summary>
    public static WorldDocument Capture(World aWorld, string aName = "", double aTime = 0) =>
        new(Format, aName, aTime, [.. aWorld.Entities.Select(CaptureEntity)]);

    private static EntityDocument CaptureEntity(Entity aEntity)
    {
        var position = Vector(aEntity.Body.Position);
        var rotation = Rotation(aEntity.Body.Rotation);
        return aEntity switch
        {
            Floor floor => new FloorDocument(floor.Id, floor.Name, position, rotation, Vector(floor.Size)),
            TargetBall target => new TargetDocument(target.Id, target.Name, position, rotation, target.Radius),
            Obstacle obstacle => new ObstacleDocument(obstacle.Id, obstacle.Name, position, rotation, obstacle.Radius, obstacle.Height),
            CarCreature car => new CarDocument(car.Id, car.Name, position, rotation, Vector(car.Color),
                WorldObjectCatalog.WhiskerCountOf(car), car.Body.Sensors.OfType<RaySensor>().FirstOrDefault()?.Range ?? WorldObjectCatalog.WhiskerRange,
                TargetOf(car), CaptureBrain(car.Brain!), DriveOf(car)),
            CylinderCreature cylinder => new CylinderDocument(cylinder.Id, cylinder.Name, position, rotation, Vector(cylinder.Color),
                TargetOf(cylinder), CaptureBrain(cylinder.Brain!), DriveOf(cylinder)),
            SnakeCreature snake => new SnakeDocument(snake.Id, snake.Name, position, rotation, Vector(snake.Color), snake.Segments,
                TargetOf(snake), CaptureBrain(snake.Brain!)),
            _ => throw new NotSupportedException($"Zapis nie zna encji {aEntity.GetType().Name}.")
        };
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
    private static BrainDocument CaptureBrain(Brain aBrain) =>
        CaptureBrain(aBrain.Graph, aBrain.Snapshots) with { Current = aBrain.CurrentSnapshot()?.Id };

    private static BrainDocument CaptureBrain(BrainGraph aGraph, IReadOnlyList<BrainSnapshot> aSnapshots, IEnumerable<BrainModule>? aSkip = null)
    {
        var skip = aSkip?.ToHashSet() ?? [];
        var modules = aGraph.Modules.Where(aModule => !skip.Contains(aModule)).Select(CaptureModule).ToList();
        var positions = aGraph.Positions.ToDictionary(aEntry => aEntry.Key, aEntry => new[] { aEntry.Value.X, aEntry.Value.Y });
        return new BrainDocument(modules, [.. aGraph.Connections], positions, [.. aSnapshots]);
    }

    private static ModuleDocument CaptureModule(BrainModule aModule) => aModule switch
    {
        SensorModule sensor => new SensorNode(sensor.Id, sensor.Name, sensor.Slot, [.. sensor.OutputPorts]),
        ActuatorModule actuator => new ActuatorNode(actuator.Id, actuator.Name, actuator.Slot, [.. actuator.InputPorts]),
        ConstantModule constant => new ConstantNode(constant.Id, constant.Name, constant.Port, constant.Value),
        RouterModule router => new RouterNode(router.Id, router.Name, router.Channels, [.. router.Ports]),
        CompositeModule composite => new CompositeNode(composite.Id, composite.Name, composite.Input.Id, composite.Output.Id,
            [.. composite.InputPorts], [.. composite.OutputPorts], CaptureBrain(composite.Inner, [], [composite.Input, composite.Output])),
        NeuralNetworkModule or CpgModule or AvoidAndSeekModule or ApproachTargetModule =>
            new StateNode(aModule.Id, aModule.Name, aModule.CaptureState()!),
        _ => throw new NotSupportedException($"Zapis nie zna modułu {aModule.GetType().Name}.")
    };

    // ---------- dokument → świat ----------

    /// <summary>Nowy świat z dokumentu. Stwory dostają zapisane mózgi (strukturę, stan i snapshoty).</summary>
    public static DemoScene Restore(WorldDocument aDocument)
    {
        var world = new World();
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
            case FloorDocument floor:
                entity = new Floor(position, ToVector(floor.Size)) { Id = floor.Id };
                break;
            case TargetDocument target:
                entity = new TargetBall { Id = target.Id, Radius = target.Radius, Body = { Position = position, Rotation = rotation } };
                break;
            case ObstacleDocument obstacle:
                entity = new Obstacle { Id = obstacle.Id, Radius = obstacle.Radius, Height = obstacle.Height, Body = { Position = position, Rotation = rotation } };
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
            case CylinderDocument cylinder:
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
                entity = creature;
                break;
            }
            default:
                throw new NotSupportedException($"Nieznany rodzaj encji w pliku: {aDocument.GetType().Name}.");
        }

        entity.AssignId(aDocument.Id);
        entity.Name = aDocument.Name;
        if (!entity.IsFixed)
            entity.Place(position, rotation);
        return entity;
    }

    /// <summary>
    /// Zastępuje graf mózgu zapisanym (moduły, połączenia, położenia), dokłada zapisane snapshoty i przywraca bieżący.
    /// </summary>
    private static void RestoreBrain(Brain aBrain, BrainDocument aDocument)
    {
        FillGraph(aBrain.Graph, aDocument);
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
            SensorNode sensor => new SensorModule(sensor.Slot, sensor.Ports) { Id = sensor.Id },
            ActuatorNode actuator => new ActuatorModule(actuator.Slot, actuator.Ports) { Id = actuator.Id },
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

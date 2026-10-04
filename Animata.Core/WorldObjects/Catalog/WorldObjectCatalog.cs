using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Bodies;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Sensors;
using Animata.Core.Worlds;

namespace Animata.Core.WorldObjects;

/// <summary>Uchwyty do demo: świat i stwory.</summary>
public sealed record DemoScene(World World, IReadOnlyList<ActiveEntity> Creatures);

// Katalog encji świata: bryły (kula, cylinder, klocek, podłoga) i budowa mózgu. Stwory i sceny są w plikach WorldObjectCatalog.*.cs.
public static partial class WorldObjectCatalog
{
    /// <summary>Kula (cel oka) leżąca na (x, y, z).</summary>
    public static Sphere CreateSphere(Vector3 aPosition, float aRadius = 0.4f) => new Spawn(EntityTypes.Of<Sphere>())
    {
        Settings = Spawn.Configure<Sphere>(aSphere => aSphere.Radius = aRadius),
        Pose = Spawn.At(aPosition)
    }.Build<Sphere>();

    /// <summary>
    /// Mózg: sensory ciała → controller → napędy ciała (węzły ciała daje <see cref="Brain.SyncBody"/>). Każde wejście
    /// controllera musi pochodzić z któregoś sensora (literówka w nazwie portu to błąd, a nie ciche zero). Napęd, którego
    /// porty controller wystawia, dostaje wszystkie (częściowo — błąd); napęd bez żadnego z nich zostaje wolny, ale controller
    /// musi sterować przynajmniej jednym.
    /// </summary>
    public static void BuildBrain(Brain aBrain, BrainModule aController)
    {
        aBrain.SyncBody();
        var graph = aBrain.Graph;
        var sources = graph.Modules.OfType<SensorModule>().ToList();
        var drives = graph.Modules.OfType<ActuatorModule>().ToList();
        graph.Modules.Insert(sources.Count, aController);
        var controller = aController;
        ConnectSenses(aBrain, controller);

        // Każdy napęd, którego porty sterownik wystawia, dostaje je wszystkie; napęd częściowo pokryty to błąd,
        // napęd bez żadnego portu sterownika zostaje wolny (np. drugi napęd dla innego modułu).
        var driven = 0;
        foreach (var drive in drives)
        {
            var provided = new List<(string Source, string Target)>();
            foreach (var port in drive.InputPorts)
            {
                var qualified = BodyBindings.Qualified(drive.Slot, port);
                if (controller.OutputPorts.Contains(qualified))
                    provided.Add((qualified, port));
                else if (controller.OutputPorts.Contains(port))
                {
                    if (drives.Count(aDrive => aDrive.InputPorts.Contains(port)) > 1)
                        throw new ArgumentException($"Output {port} matches multiple actuators; use slot.port.", nameof(aController));
                    provided.Add((port, port));
                }
            }
            if (provided.Count == 0)
                continue;
            if (provided.Count < drive.InputPorts.Count)
                throw new ArgumentException(
                    $"{controller} drives {drive.Slot} only partly; missing outputs: {string.Join(", ", drive.InputPorts.Except(provided.Select(aPort => aPort.Target)))}.",
                    nameof(aController));
            foreach (var (source, target) in provided)
                graph.Connect(controller, source, drive, target);
            driven++;
        }
        if (driven == 0 && drives.Count > 0)
            throw new ArgumentException(
                $"{controller} has no outputs for any actuator ({string.Join(", ", drives.SelectMany(aDrive => aDrive.InputPorts))}).",
                nameof(aController));
        graph.Validate();
    }

    /// <summary>
    /// Podpina każde wejście modułu (już dodanego do grafu mózgu) do zmysłu ciała z portem tej samej nazwy. Wejście, którego
    /// żaden zmysł nie daje (literówka w porcie), to <see cref="ArgumentException"/> z listą — a nie ciche zero.
    /// </summary>
    public static void ConnectSenses(Brain aBrain, BrainModule aModule)
    {
        var sources = aBrain.Graph.Modules.OfType<SensorModule>().ToList();
        var missing = new List<string>();
        foreach (var port in aModule.InputPorts)
        {
            var matches = sources.SelectMany(aSource => aSource.OutputPorts
                .Where(aPort => BodyBindings.Qualified(aSource.Slot, aPort) == port)
                .Select(aPort => (Source: aSource, Port: aPort))).ToArray();
            if (matches.Length == 0)
                matches = sources.Where(aSource => aSource.OutputPorts.Contains(port))
                    .Select(aSource => (Source: aSource, Port: port)).ToArray();
            if (matches.Length > 1)
                throw new ArgumentException($"Input {port} matches multiple sensors; use slot.port.", nameof(aModule));
            if (matches.Length == 0)
                missing.Add(port);
            else
                aBrain.Graph.Connect(matches[0].Source, matches[0].Port, aModule, port);
        }
        if (missing.Count > 0)
            throw new ArgumentException($"{aModule} has inputs no sensor provides: {string.Join(", ", missing)}.", nameof(aModule));
    }

    /// <summary>
    /// Stwór z projektu: oczy na celu, mózg sensory → <paramref name="aController"/> → napędy (<see cref="BuildBrain"/>),
    /// poza. <paramref name="aSettings"/> — ustawienia projektu (np. segmenty węża), <paramref name="aColor"/> — kolor
    /// (null — z projektu), <paramref name="aSlots"/> — ustawienia gniazd przed celowaniem oczu (np. kąty wąsów).
    /// </summary>
    public static Creature Create(CreatureDesign aDesign, Vector3 aPosition, float aYaw, Guid? aTargetId, BrainModule aController,
        Vector3? aColor = null, Action<Creature>? aSettings = null, Action<ActiveEntity>? aSlots = null) =>
        Create(aDesign, aPosition, aYaw, aTargetId, Spawn.Controller(aController), aColor, aSettings, aSlots);

    /// <summary>Stwór z projektu z gotowym mózgiem o podanej nazwie (<see cref="ActiveEntity.BrainPresets"/>), oczy na celu.</summary>
    public static Creature Create(CreatureDesign aDesign, Vector3 aPosition, float aYaw, Guid? aTargetId, string aPreset) =>
        Create(aDesign, aPosition, aYaw, aTargetId, Spawn.Preset(aCreature => aCreature.BrainPresets.Single(aChoice => aChoice.Name == aPreset)));

    private static Creature Create(CreatureDesign aDesign, Vector3 aPosition, float aYaw, Guid? aTargetId, Action<ActiveEntity> aBrain,
        Vector3? aColor = null, Action<Creature>? aSettings = null, Action<ActiveEntity>? aSlots = null) => new Spawn(aDesign.Type)
    {
        Settings = aColor is null && aSettings is null ? null : Spawn.Configure<Creature>(aCreature =>
        {
            aSettings?.Invoke(aCreature);
            if (aColor is { } color)
                aCreature.Color = color;
        }),
        Slots = Spawn.Then(aSlots, Spawn.Aim(aTargetId)),
        Brain = aBrain,
        Pose = Spawn.At(aPosition, aYaw)
    }.Build<Creature>();

    /// <summary>
    /// Stwór gotowy do nauki od zera: bieżące (ręczne) parametry modułu zapisane jako snapshot <paramref name="aLabel"/>,
    /// moduł wylosowany.
    /// </summary>
    public static Creature WithHandSnapshot(Creature aCreature, BrainModule aModule, string aLabel = "ręczne parametry")
    {
        aCreature.Brain!.Capture(aLabel, aModule);
        ((ITrainableModule)aModule).Randomize();
        return aCreature;
    }

    /// <summary>Oczy stwora (<see cref="TargetSensor"/>) patrzą na podaną encję (null — bez celu).</summary>
    public static void Aim(ActiveEntity aCreature, Guid? aTargetId)
    {
        foreach (var eye in aCreature.Body.Sensors.OfType<TargetSensor>())
            eye.TargetId = aTargetId;
    }

    /// <summary>Pionowy cylinder stojący na (x, y, z).</summary>
    public static Cylinder CreateCylinder(Vector3 aPosition, float aRadius = 0.5f, float aHeight = 0.8f) => new Spawn(EntityTypes.Of<Cylinder>())
    {
        Settings = Spawn.Configure<Cylinder>(aCylinder =>
        {
            aCylinder.Radius = aRadius;
            aCylinder.Height = aHeight;
        }),
        Pose = Spawn.At(aPosition)
    }.Build<Cylinder>();

    /// <summary>Klocek: środek spodu w (x, y, z), wymiary, obrót wokół pionu.</summary>
    public static Box CreateBox(Vector3 aPosition, Vector3 aSize, float aYaw = 0) => new Spawn(EntityTypes.Of<Box>())
    {
        Settings = Spawn.Configure<Box>(aBox => aBox.Size = aSize),
        Pose = Spawn.At(aPosition, aYaw)
    }.Build<Box>();

    /// <summary>Kolor podłogi.</summary>
    public static readonly Vector3 FloorColor = new(0.20f, 0.27f, 0.33f);

    /// <summary>Podłoga: zablokowany klocek szer. × głęb. × 0.2 m z górną ścianą na z = 0, środek w (0, 0).</summary>
    public static Box CreateFloor(float aWidth, float aDepth) => new Spawn(EntityTypes.Of<Box>())
    {
        Settings = Spawn.Configure<Box>(aBox =>
        {
            aBox.Size = new Vector3(aWidth, aDepth, 0.2f);
            aBox.Color = FloorColor;
            aBox.Locked = true;
        }),
        Name = "Podłoga",
        Pose = Spawn.At(new Vector3(0, 0, -0.2f))
    }.Build<Box>();

    /// <summary>
    /// Losowy kolor stwora (odcień dowolny, nasycenie i jasność umiarkowane). Kolor nic nie znaczy — ustawia go użytkownik,
    /// na początku jest losowy.
    /// </summary>
    public static Vector3 RandomColor(Random? aRandom = null)
    {
        var random = aRandom ?? Random.Shared;
        var hue = random.NextSingle() * 6;
        const float saturation = 0.55f;
        const float value = 0.85f;
        var chroma = value * saturation;
        var x = chroma * (1 - MathF.Abs(hue % 2 - 1));
        var (r, g, b) = (int)hue switch
        {
            0 => (chroma, x, 0f),
            1 => (x, chroma, 0f),
            2 => (0f, chroma, x),
            3 => (0f, x, chroma),
            4 => (x, 0f, chroma),
            _ => (chroma, 0f, x)
        };
        var m = value - chroma;
        return new Vector3(r + m, g + m, b + m);
    }

    /// <summary>Warstwy ukryte domyślnych sieci stworów wstawianych do sceny (dwie warstwy).</summary>
    public static readonly int[] DefaultCarHidden = [12, 8];
    public static readonly int[] DefaultCylinderHidden = [8, 8];
    public static readonly int[] DefaultSpiderHidden = [16, 12];
}

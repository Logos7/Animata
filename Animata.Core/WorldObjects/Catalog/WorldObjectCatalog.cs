using System.Numerics;
using Animata.Core.Actuators;
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
    public static Sphere CreateSphere(Vector3 aPosition, float aRadius = 0.4f) => new()
    {
        Radius = aRadius,
        Body = { Position = aPosition }
    };


    /// <summary>
    /// Mózg: sensory ciała → controller → jedyny napęd ciała (węzły ciała daje <see cref="Brain.SyncBody"/>). Każde wejście
    /// controllera musi pochodzić z któregoś sensora (literówka w nazwie portu to błąd, a nie ciche zero), a controller
    /// musi mieć wszystkie wyjścia napędu.
    /// </summary>
    public static void BuildBrain(Brain aBrain, BrainModule aController)
    {
        aBrain.SyncBody();
        var graph = aBrain.Graph;
        var sources = graph.Modules.OfType<SensorModule>().ToList();
        var wheels = graph.Modules.OfType<ActuatorModule>().Single();
        graph.Modules.Insert(sources.Count, aController);
        var controller = aController;

        var missing = new List<string>();
        foreach (var port in controller.InputPorts)
        {
            var source = sources.FirstOrDefault(aSource => aSource.OutputPorts.Contains(port));
            if (source is null)
                missing.Add(port);
            else
                graph.Connect(source, port, controller, port);
        }
        if (missing.Count > 0)
            throw new ArgumentException(
                $"{controller} has inputs no sensor provides: {string.Join(", ", missing)}.", nameof(aController));

        graph.Connect(controller, wheels, wheels.InputPorts.ToArray());
        graph.Validate();
    }

    /// <summary>Oczy stwora (<see cref="TargetSensor"/>) patrzą na podaną encję (null — bez celu).</summary>
    public static void Aim(ActiveEntity aCreature, Guid? aTargetId)
    {
        foreach (var eye in aCreature.Body.Sensors.OfType<TargetSensor>())
            eye.TargetId = aTargetId;
    }

    /// <summary>Pionowy cylinder stojący na (x, y, z).</summary>
    public static Cylinder CreateCylinder(Vector3 aPosition, float aRadius = 0.5f, float aHeight = 0.8f) => new()
    {
        Radius = aRadius,
        Height = aHeight,
        Body = { Position = aPosition }
    };

    /// <summary>Klocek: środek spodu w (x, y, z), wymiary, obrót wokół pionu.</summary>
    public static Box CreateBox(Vector3 aPosition, Vector3 aSize, float aYaw = 0) => new()
    {
        Size = aSize,
        Body = { Position = aPosition, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, aYaw) }
    };

    /// <summary>Kolor podłogi.</summary>
    public static readonly Vector3 FloorColor = new(0.20f, 0.27f, 0.33f);

    /// <summary>Podłoga: zablokowany klocek szer. × głęb. × 0.2 m z górną ścianą na z = 0, środek w (0, 0).</summary>
    public static Box CreateFloor(float aWidth, float aDepth) => new()
    {
        Name = "Podłoga",
        Size = new Vector3(aWidth, aDepth, 0.2f),
        Color = FloorColor,
        Locked = true,
        Body = { Position = new Vector3(0, 0, -0.2f) }
    };

    /// <summary>Wyjścia sieci sterującej stawami (wąż, pająk): Yaw0, Pitch0, Yaw1, Pitch1, … — nowy staw dopisuje się na końcu.</summary>
    public static string[] JointNetworkOutputs(int aJoints) =>
        [.. Enumerable.Range(0, aJoints).SelectMany(aJoint => new[] { SpineActuator.YawPort(aJoint), SpineActuator.PitchPort(aJoint) })];

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

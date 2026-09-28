using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Bodies;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.Entities;
using Animata.Core.Sensors;
using Animata.Core.Worlds;

namespace Animata.Core.WorldObjects;

/// <summary>Uchwyty do demo: świat i stwory.</summary>
public sealed record DemoScene(World World, IReadOnlyList<ActiveEntity> Creatures);

// Katalog encji świata: wspólne klocki (cel, słupek, płyta, budowa mózgu). Stwory i sceny są w plikach WorldObjectCatalog.*.cs.
public static partial class WorldObjectCatalog
{
    private static readonly string[] TargetPorts =
    [
        TargetSensor.FoundPort, TargetSensor.GapPort, TargetSensor.DirectionXPort, TargetSensor.DirectionYPort
    ];

    public static TargetBall CreateTargetBall(Vector3 aPosition) => new()
    {
        Body = { Position = aPosition }
    };


    /// <summary>
    /// Mózg: sensory → controller → napęd. Każde wejście controllera musi pochodzić z któregoś sensora
    /// (literówka w nazwie portu to błąd, a nie ciche zero), a controller musi mieć wszystkie wyjścia napędu.
    /// </summary>
    public static void BuildBrain(Brain aBrain, IReadOnlyList<(string Name, Sensor Sensor)> aSensors,
        BrainModule aController, Actuator aWheels)
    {
        var graph = aBrain.Graph;
        var sources = aSensors.Select(aEntry => graph.Add(new SensorModule(aEntry.Sensor) { Name = aEntry.Name })).ToArray();
        var controller = graph.Add(aController);
        var wheels = graph.Add(new ActuatorModule(aWheels) { Name = aWheels.Slot });

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

    public static Obstacle CreateObstacle(Vector3 aPosition, float aRadius = 0.5f) => new()
    {
        Radius = aRadius,
        Body = { Position = aPosition }
    };

    /// <summary>Płyta terenu: środek spodu w (x, y, z), wymiary, obrót wokół pionu.</summary>
    public static Slab CreateSlab(Vector3 aPosition, Vector3 aSize, float aYaw = 0) => new()
    {
        Size = aSize,
        Body = { Position = aPosition, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, aYaw) }
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

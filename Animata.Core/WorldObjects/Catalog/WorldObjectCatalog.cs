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

    public static readonly Vector3 ControllerColor = new(0.24f, 0.68f, 0.9f);
    public static readonly Vector3 NeuralColor = new(0.72f, 0.36f, 0.9f);

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
}

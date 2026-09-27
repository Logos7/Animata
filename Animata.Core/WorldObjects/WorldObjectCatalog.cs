using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.Entities;
using Animata.Core.Sensors;
using Animata.Core.Worlds;

namespace Animata.Core.WorldObjects;

/// <summary>Uchwyty do demo: świat i stwory.</summary>
public sealed record DemoScene(World World, IReadOnlyList<ActiveEntity> Creatures);

public static class WorldObjectCatalog
{
    private static readonly string[] TargetPorts =
    [
        TargetSensor.FoundPort, TargetSensor.GapPort, TargetSensor.DirectionXPort, TargetSensor.DirectionYPort
    ];

    /// <summary>Domyślna liczba wąsów autka: 5 promieni co 30° (−60°…+60°) — na niej dobrano parametry sterownika.</summary>
    public const int DefaultWhiskers = 5;

    /// <summary>Najwięcej wąsów autka (co 5° w wachlarzu 120°).</summary>
    public const int MaxWhiskers = 25;

    /// <summary>Szerokość wachlarza wąsów autka (rad), niezależnie od ich liczby.</summary>
    public const float WhiskerSpread = 120 * MathF.PI / 180;

    public const float WhiskerRange = 3;

    /// <summary>Dozwolone liczby wąsów autka: 1, 3, …, <see cref="MaxWhiskers"/>.</summary>
    public static IReadOnlyList<int> WhiskerCounts { get; } =
        [.. Enumerable.Range(0, MaxWhiskers / 2 + 1).Select(aIndex => 2 * aIndex + 1)];

    /// <summary>Nieparzysta, od 1 do <see cref="MaxWhiskers"/> — środkowy wąs zawsze patrzy prosto przed maskę.</summary>
    public static bool IsValidWhiskerCount(int aCount) => aCount is >= 1 and <= MaxWhiskers && aCount % 2 == 1;

    /// <summary>Kąty wąsów autka: <paramref name="aCount"/> promieni równo w wachlarzu <see cref="WhiskerSpread"/>.</summary>
    public static float[] WhiskerAnglesFor(int aCount)
    {
        if (!IsValidWhiskerCount(aCount))
            throw new ArgumentOutOfRangeException(nameof(aCount), aCount,
                $"Autko ma nieparzystą liczbę wąsów od 1 do {MaxWhiskers}.");
        return [.. RaySensor.Fan(aCount, WhiskerSpread).Angles];
    }

    /// <summary>Liczba wąsów stwora (promieni jego pierwszego <see cref="RaySensor"/>), 0 — bez wąsów.</summary>
    public static int WhiskerCountOf(Entity aEntity) =>
        aEntity.Body.Sensors.OfType<RaySensor>().FirstOrDefault()?.Angles.Count ?? 0;

    public static TargetBall CreateTargetBall(Vector3 aPosition) => new()
    {
        Body = { Position = aPosition }
    };

    /// <summary>
    /// Sieć 3-6-2 z ręcznie ustawionymi wagami startowymi (punkt wyjścia do ewolucji):
    /// Turn ≈ tanh(2·tanh(3·DirectionY)), Step ≈ tanh(2·tanh(2·Gap) + 2·tanh(3·DirectionX) − 2).
    /// Step spada do zera przy styku z celem i gdy cel jest z boku lub z tyłu.
    /// </summary>
    public static NeuralNetworkModule CreateNeuralModule()
    {
        var module = new NeuralNetworkModule(new NeuralNetwork(3, 6, 2)) { Name = "Neural" };
        module.Ports.AddRange(TargetPorts);
        module.Inputs.Add(new NeuralInput("Found * DirectionY"));
        module.Inputs.Add(new NeuralInput("Found * Gap"));
        module.Inputs.Add(new NeuralInput("Found * DirectionX"));
        module.Outputs.Add(new NeuralOutput(DiskDriveActuator.TurnPort));
        module.Outputs.Add(new NeuralOutput(DiskDriveActuator.StepPort));

        var weights = module.Network.Weights;
        var biases = module.Network.Biases;
        for (var layer = 0; layer < weights.Length; layer++)
            for (var neuron = 0; neuron < weights[layer].Length; neuron++)
            {
                Array.Fill(weights[layer][neuron], 0);
                biases[layer][neuron] = 0;
            }

        weights[0][0][0] = 3;   // h0 ← kierunek w bok
        weights[0][1][1] = 2;   // h1 ← szczelina
        weights[0][2][2] = 3;   // h2 ← kierunek do przodu
        weights[1][0][0] = 2;   // Turn ← h0
        weights[1][1][1] = 2;   // Step ← h1
        weights[1][1][2] = 2;   // Step ← h2
        biases[1][1] = -2;      // Step wymaga jednocześnie szczeliny i celu z przodu
        return module;
    }

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
        var wheels = graph.Add(new ActuatorModule(aWheels) { Name = "Wheels" });

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

    /// <summary>Stwór z okiem namierzającym cel i napędem różnicowym, sterowany podanym modułem.</summary>
    public static CylinderCreature CreateSeeker(Vector3 aPosition, Vector3 aColor, Guid? aTargetId, BrainModule aController)
    {
        var brain = new Brain();
        var creature = new CylinderCreature(brain) { Color = aColor, Body = { Position = aPosition } };
        var eye = new TargetSensor { TargetId = aTargetId };
        var wheels = new DiskDriveActuator();
        creature.Body.Sensors.Add(eye);
        creature.Body.Actuators.Add(wheels);
        BuildBrain(brain, [("Eye", eye)], aController, wheels);
        return creature;
    }

    public static CylinderCreature CreateControllerSeeker(Vector3 aPosition, Guid? aTargetId) =>
        CreateSeeker(aPosition, ControllerColor, aTargetId, new ApproachTargetModule { Name = "Approach" });

    public static CylinderCreature CreateNeuralSeeker(Vector3 aPosition, Guid? aTargetId) =>
        CreateSeeker(aPosition, NeuralColor, aTargetId, CreateNeuralModule());

    /// <summary>
    /// Walec z siecią gotową do nauki: ręczne wagi zapisane w mózgu jako snapshot „ręczne wagi”, sieć wylosowana.
    /// </summary>
    public static CylinderCreature CreateLearningSeeker(Vector3 aPosition, Guid? aTargetId)
    {
        var creature = CreateNeuralSeeker(aPosition, aTargetId);
        var network = creature.Brain!.Graph.Modules.OfType<NeuralNetworkModule>().Single();
        creature.Brain.Capture("ręczne wagi", network);
        network.Network.Randomize();
        return creature;
    }

    // ---------- autka ----------

    public static Obstacle CreateObstacle(Vector3 aPosition, float aRadius = 0.5f) => new()
    {
        Radius = aRadius,
        Body = { Position = aPosition }
    };

    /// <summary>
    /// Autko z okiem (TargetSensor), <paramref name="aWhiskers"/> wąsami (RaySensor) i kierownicą (SteeringDriveActuator).
    /// Mózg: eye + whiskers → controller → wheels. Controller może czytać dowolne porty celu i Ray{i},
    /// więc musi być zrobiony dla tej samej liczby wąsów (inaczej <see cref="BuildBrain"/> wymieni brakujące porty).
    /// </summary>
    public static CarCreature CreateCar(Vector3 aPosition, float aYaw, Vector3 aColor, Guid? aTargetId, BrainModule aController,
        int aWhiskers = DefaultWhiskers)
    {
        var brain = new Brain();
        var car = new CarCreature(brain)
        {
            Color = aColor,
            Body = { Position = aPosition, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, aYaw) }
        };
        var eye = new TargetSensor { TargetId = aTargetId };
        var whiskers = new RaySensor(WhiskerAnglesFor(aWhiskers), WhiskerRange);
        var wheels = new SteeringDriveActuator();
        car.Body.Sensors.Add(eye);
        car.Body.Sensors.Add(whiskers);
        car.Body.Actuators.Add(wheels);
        BuildBrain(brain, [("Eye", eye), ("Whiskers", whiskers)], aController, wheels);
        return car;
    }

    public static AvoidAndSeekModule CreateAvoidController(int aWhiskers = DefaultWhiskers) =>
        new(WhiskerAnglesFor(aWhiskers)) { Name = "AvoidAndSeek" };

    /// <summary>
    /// Sieć autka (3 + wąsy)-8-2 z losowymi wagami: wejścia to kierunek do celu (bok, przód), szczelina/4
    /// i po jednym wejściu na wąs; wyjścia Steer, Throttle (tanh, więc także cofanie). Dla 5 wąsów: 8-8-2, 90 parametrów.
    /// </summary>
    public static NeuralNetworkModule CreateCarNeuralModule(int aWhiskers = DefaultWhiskers)
    {
        var rays = Enumerable.Range(0, WhiskerAnglesFor(aWhiskers).Length).Select(RaySensor.PortName).ToArray();
        var module = new NeuralNetworkModule(new NeuralNetwork(3 + rays.Length, 8, 2)) { Name = "Neural" };
        module.Ports.AddRange(TargetPorts);
        module.Ports.AddRange(rays);
        module.Inputs.Add(new NeuralInput("Found * DirectionY"));
        module.Inputs.Add(new NeuralInput("Found * DirectionX"));
        module.Inputs.Add(new NeuralInput("Found * Gap / 4"));
        foreach (var ray in rays)
            module.Inputs.Add(new NeuralInput(ray));
        module.Outputs.Add(new NeuralOutput(SteeringDriveActuator.SteerPort));
        module.Outputs.Add(new NeuralOutput(SteeringDriveActuator.ThrottlePort));
        return module;
    }

    public static CarCreature CreateControllerCar(Vector3 aPosition, float aYaw, Guid? aTargetId, int aWhiskers = DefaultWhiskers) =>
        CreateCar(aPosition, aYaw, ControllerColor, aTargetId, CreateAvoidController(aWhiskers), aWhiskers);

    public static CarCreature CreateNeuralCar(Vector3 aPosition, float aYaw, Guid? aTargetId, int aWhiskers = DefaultWhiskers) =>
        CreateCar(aPosition, aYaw, NeuralColor, aTargetId, CreateCarNeuralModule(aWhiskers), aWhiskers);

    // ---------- demo ----------

    /// <summary>
    /// Dwa tory, żeby stwory nie tłoczyły się przy jednym celu:
    /// - dół: kulka za czterema słupkami i dwa autka z wąsami (sterownik i losowa sieć),
    /// - góra: wolny tor z własną kulką i dwa walce bez wąsów (sterownik i losowa sieć).
    /// Ręczne wagi sieci walca są zapisane w jej mózgu jako snapshot „ręczne wagi”.
    /// </summary>
    public static DemoScene CreateDemo()
    {
        var world = new World();
        var carTarget = CreateTargetBall(new Vector3(5, -1.5f, 0));
        var cylinderTarget = CreateTargetBall(new Vector3(5, 5, 0));
        carTarget.Name = "Kulka (dół)";
        cylinderTarget.Name = "Kulka (góra)";
        world.Add(carTarget);
        world.Add(cylinderTarget);

        world.Add(CreateObstacle(new Vector3(1.5f, -1.3f, 0), 0.8f));
        world.Add(CreateObstacle(new Vector3(-0.5f, 0.6f, 0), 0.5f));
        world.Add(CreateObstacle(new Vector3(-0.5f, -3.9f, 0), 0.6f));
        world.Add(CreateObstacle(new Vector3(3.4f, -3.5f, 0), 0.4f));

        var controllerCar = CreateControllerCar(new Vector3(-5, -2.7f, 0), 0, carTarget.Id);
        var neuralCar = CreateNeuralCar(new Vector3(-5, -0.3f, 0), 0, carTarget.Id);
        var controller = CreateControllerSeeker(new Vector3(-5, 3.8f, 0), cylinderTarget.Id);
        var neural = CreateLearningSeeker(new Vector3(-5, 6.2f, 0), cylinderTarget.Id);
        controllerCar.Name = "Autko sterownik";
        neuralCar.Name = "Autko NN";
        controller.Name = "Walec sterownik";
        neural.Name = "Walec NN";

        ActiveEntity[] creatures = [controllerCar, neuralCar, controller, neural];
        foreach (var creature in creatures)
            world.Add(creature);
        return new DemoScene(world, creatures);
    }
}

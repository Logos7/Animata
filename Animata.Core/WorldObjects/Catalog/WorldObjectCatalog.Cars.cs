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

// Autka: wąsy (liczba, kąty) i gotowe autka ze sterownikiem albo siecią.
public static partial class WorldObjectCatalog
{
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

    /// <summary>
    /// Autko z okiem (TargetSensor), <paramref name="aWhiskers"/> wąsami (RaySensor) i kierownicą (SteeringDriveActuator).
    /// Mózg: eye + whiskers → controller → wheels. Controller może czytać dowolne porty celu i Ray{i},
    /// więc musi być zrobiony dla tej samej liczby wąsów (inaczej <see cref="BuildBrain"/> wymieni brakujące porty).
    /// </summary>
    public static CarCreature CreateCar(Vector3 aPosition, float aYaw, Vector3 aColor, Guid? aTargetId, BrainModule aController,
        int aWhiskers = DefaultWhiskers)
    {
        var brain = new Brain();
        var car = new CarCreature(brain) { Color = aColor };
        car.Place(aPosition, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, aYaw));
        var eye = new TargetSensor { Slot = "Eye", TargetId = aTargetId };
        var whiskers = new RaySensor(WhiskerAnglesFor(aWhiskers), WhiskerRange) { Slot = "Whiskers" };
        var wheels = new SteeringDriveActuator { Slot = "Wheels" };
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
    public static NeuralNetworkModule CreateCarNeuralModule(int aWhiskers = DefaultWhiskers, params int[] aHidden)
    {
        var rays = Enumerable.Range(0, WhiskerAnglesFor(aWhiskers).Length).Select(RaySensor.PortName).ToArray();
        int[] hidden = aHidden.Length > 0 ? aHidden : [8];
        var module = new NeuralNetworkModule(new NeuralNetwork([3 + rays.Length, .. hidden, 2])) { Name = "Neural" };
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
        CreateCar(aPosition, aYaw, RandomColor(), aTargetId, CreateAvoidController(aWhiskers), aWhiskers);

    public static CarCreature CreateNeuralCar(Vector3 aPosition, float aYaw, Guid? aTargetId, int aWhiskers = DefaultWhiskers) =>
        CreateCar(aPosition, aYaw, RandomColor(), aTargetId, CreateCarNeuralModule(aWhiskers), aWhiskers);
}

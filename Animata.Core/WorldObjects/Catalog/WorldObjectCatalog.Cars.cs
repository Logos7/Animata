using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Brains.Modules;
using Animata.Core.Sensors;

namespace Animata.Core.WorldObjects;

// Autka: gotowe autka ze sterownikiem albo siecią (wąsy — projekt Car).
public static partial class WorldObjectCatalog
{
    /// <summary>
    /// Autko z okiem (TargetSensor), <paramref name="aWhiskers"/> wąsami (RaySensor) i kierownicą (SteeringDriveActuator).
    /// Mózg: eye + whiskers → controller → wheels. Controller może czytać dowolne porty celu i Ray{i},
    /// więc musi być zrobiony dla tej samej liczby wąsów (inaczej <see cref="BuildBrain"/> wymieni brakujące porty).
    /// </summary>
    public static Creature CreateCar(Vector3 aPosition, float aYaw, Vector3 aColor, Guid? aTargetId, BrainModule aController,
        int aWhiskers = Car.DefaultWhiskers)
    {
        var angles = Car.WhiskerAnglesFor(aWhiskers);
        return Create(Car.Design, aPosition, aYaw, aTargetId, aController, aColor,
            aSlots: aCar => aCar.Body.Sensors.OfType<RaySensor>().Single().SetAngles(angles));
    }

    public static AvoidAndSeekModule CreateAvoidController(int aWhiskers = Car.DefaultWhiskers) =>
        new(Car.WhiskerAnglesFor(aWhiskers)) { Name = "AvoidAndSeek" };

    /// <summary>
    /// Sieć autka (3 + wąsy)-8-2 z losowymi wagami: wejścia to kierunek do celu (bok, przód), szczelina/4
    /// i po jednym wejściu na wąs; wyjścia Steer, Throttle (tanh, więc także cofanie). Dla 5 wąsów: 8-8-2, 90 parametrów.
    /// </summary>
    public static NeuralNetworkModule CreateCarNeuralModule(int aWhiskers = Car.DefaultWhiskers, params int[] aHidden)
    {
        var rays = Enumerable.Range(0, Car.WhiskerAnglesFor(aWhiskers).Length).Select(RaySensor.PortName).ToArray();
        return NeuralNetworkModule.Build("Neural", [.. TargetSensor.SteeringPorts, .. rays],
            ["Found * DirectionY", "Found * DirectionX", "Found * Gap / 4", .. rays],
            [SteeringDriveActuator.SteerPort, SteeringDriveActuator.ThrottlePort], aHidden.Length > 0 ? aHidden : [8]);
    }

    public static Creature CreateControllerCar(Vector3 aPosition, float aYaw, Guid? aTargetId, int aWhiskers = Car.DefaultWhiskers) =>
        CreateCar(aPosition, aYaw, RandomColor(), aTargetId, CreateAvoidController(aWhiskers), aWhiskers);

    public static Creature CreateNeuralCar(Vector3 aPosition, float aYaw, Guid? aTargetId, int aWhiskers = Car.DefaultWhiskers) =>
        CreateCar(aPosition, aYaw, RandomColor(), aTargetId, CreateCarNeuralModule(aWhiskers), aWhiskers);
}

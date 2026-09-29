using System.Numerics;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.Sensors;
using Animata.Core.Training;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Tests;

/// <summary>Autko z dowolną nieparzystą liczbą wąsów (1…25).</summary>
public class WhiskerTests
{
    public static TheoryData<int> ValidCounts()
    {
        var data = new TheoryData<int>();
        foreach (var count in WorldObjectCatalog.WhiskerCounts)
            data.Add(count);
        return data;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(26)]
    [InlineData(27)]
    public void InvalidCount_Throws(int aCount)
    {
        Assert.False(WorldObjectCatalog.IsValidWhiskerCount(aCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldObjectCatalog.WhiskerAnglesFor(aCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldObjectCatalog.CreateControllerCar(Vector3.Zero, 0, null, aCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => SeekRigs.CarWith(aCount));
    }

    [Theory]
    [MemberData(nameof(ValidCounts))]
    public void Angles_AreSymmetricFanWithMiddleStraightAhead(int aCount)
    {
        var angles = WorldObjectCatalog.WhiskerAnglesFor(aCount);

        Assert.Equal(aCount, angles.Length);
        Assert.True(MathF.Abs(angles[aCount / 2]) < 1e-5f);
        for (var index = 0; index < aCount; index++)
            Assert.Equal(-angles[aCount - 1 - index], angles[index], 5);
        if (aCount > 1)
        {
            Assert.Equal(-WorldObjectCatalog.WhiskerSpread / 2, angles[0], 5);
            Assert.Equal(WorldObjectCatalog.WhiskerSpread / 2, angles[^1], 5);
        }
    }

    [Theory]
    [MemberData(nameof(ValidCounts))]
    public void Cars_WithAnyValidCount_BuildAndRun(int aCount)
    {
        var world = new World();
        var target = WorldObjectCatalog.CreateSphere(new Vector3(6, 0, 0));
        world.Add(target);
        var controllerCar = WorldObjectCatalog.CreateControllerCar(Vector3.Zero, 0, target.Id, aCount);
        var neuralCar = WorldObjectCatalog.CreateNeuralCar(new Vector3(0, 3, 0), 0, target.Id, aCount);
        world.Add(controllerCar);
        world.Add(neuralCar);

        for (var tick = 0; tick < 10; tick++)
            world.Update(1f / 30f);

        Assert.Equal(aCount, WorldObjectCatalog.WhiskerCountOf(controllerCar));
        Assert.Equal(aCount, WorldObjectCatalog.WhiskerCountOf(neuralCar));
        var network = neuralCar.Brain!.Graph.Modules.OfType<NeuralNetworkModule>().Single();
        Assert.Equal((3 + aCount) * 8 + 8 + 8 * 2 + 2, network.Network.ParameterCount);
    }

    [Fact]
    public void TrainingRig_MatchesTheCarsWhiskers()
    {
        var car = WorldObjectCatalog.CreateNeuralCar(Vector3.Zero, 0, null, 9);
        var rig = SeekRigs.For(car);
        var template = (NeuralNetworkState)car.Brain!.Graph.Modules.OfType<NeuralNetworkModule>().Single().CaptureState();

        Assert.Same(SeekRigs.CarWith(9), rig);
        Assert.Equal(9, WorldObjectCatalog.WhiskerCountOf(rig.CreateCreature(Guid.Empty, WorldObjectCatalog.CreateAvoidController(9))));
        _ = new SeekTargetTask(template, rig);

        // Sieć z 9 wejściami Ray{i} nie zbuduje się w ciele z 5 wąsami — błąd, nie ciche zera.
        Assert.Throws<ArgumentException>(() => new SeekTargetTask(template, SeekRigs.Car));
        Assert.Same(SeekRigs.Car, SeekRigs.For(WorldObjectCatalog.CreateNeuralCar(Vector3.Zero, 0, null)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    public void Controller_WithUnusualWhiskerCount_ReachesTargetOnOpenRoad(int aCount)
    {
        var rig = SeekRigs.CarWith(aCount);
        var result = SeekTargetTask.Run(WorldObjectCatalog.CreateAvoidController(aCount), [TestWorlds.OpenRoad()], rig.DefaultOptions, rig)[0];

        Assert.True(result.Reached, $"gap {result.FinalGap:0.00}");
    }

    // ---------- zmiana liczby wąsów w miejscu ----------

    [Fact]
    public void SetCount_KeepsTheSameCreatureSensorAndController()
    {
        var world = new World();
        var target = WorldObjectCatalog.CreateSphere(new Vector3(6, 0, 0));
        world.Add(target);
        var car = WorldObjectCatalog.CreateControllerCar(Vector3.Zero, 0, target.Id);
        world.Add(car);
        var sensor = car.Body.Sensors.OfType<RaySensor>().Single();
        var controller = car.Brain!.Graph.Modules.OfType<AvoidAndSeekModule>().Single();
        controller.AvoidGain = 3;

        WhiskerRewiring.SetCount(car, 9);

        Assert.Same(sensor, car.Body.Sensors.OfType<RaySensor>().Single());
        Assert.Equal(9, sensor.Angles.Count);
        Assert.Same(controller, car.Brain.Graph.Modules.OfType<AvoidAndSeekModule>().Single());
        Assert.Equal(9, controller.RayAngles.Count);
        Assert.Equal(3, controller.AvoidGain);
        var source = car.Brain.Graph.Modules.OfType<SensorModule>().Single(aModule => aModule.Slot == sensor.Slot);
        Assert.Equal(9, source.OutputPorts.Count);
        for (var ray = 0; ray < 9; ray++)
            Assert.Contains(car.Brain.Graph.Connections, aLink =>
                aLink.SourceId == source.Id && aLink.TargetId == controller.Id && aLink.TargetPort == RaySensor.PortName(ray));
        car.Brain.Graph.Validate();
        for (var tick = 0; tick < 10; tick++)
            world.Update(1f / 30f);

        WhiskerRewiring.SetCount(car, 3);
        Assert.Equal(3, sensor.Angles.Count);
        Assert.DoesNotContain(car.Brain.Graph.Connections, aLink => aLink.SourcePort == RaySensor.PortName(3));
        world.Update(1f / 30f);
    }

    [Fact]
    public void SetCount_NetworkKeepsItsWeightsForTheTarget()
    {
        var car = WorldObjectCatalog.CreateNeuralCar(Vector3.Zero, 0, null);
        var network = car.Brain!.Graph.Modules.OfType<NeuralNetworkModule>().Single();
        float[] Output(int aRays) =>
            network.Network.Evaluate([0.3f, 0.8f, 0.5f, .. new float[aRays]]).ToArray();
        var before = Output(5);

        WhiskerRewiring.SetCount(car, 11);

        Assert.Same(network, car.Brain.Graph.Modules.OfType<NeuralNetworkModule>().Single());
        Assert.Equal(3 + 11, network.Network.Layers[0]);
        Assert.Equal(11, network.Ports.Count(RaySensor.IsPortName));
        // Wolna droga (wąsy = 0): sieć jedzie do celu dokładnie tak jak przed zmianą.
        Assert.Equal(before, Output(11));
        car.Brain.Graph.Validate();

        // Nauka po zmianie: rig ma 11 wąsów, a szablon sieci się w nim buduje.
        _ = new SeekTargetTask((NeuralNetworkState)network.CaptureState(), SeekRigs.For(car));
    }

    [Fact]
    public void SetCount_ConvertsSnapshotsSoTheyStillRestore()
    {
        var car = WorldObjectCatalog.CreateNeuralCar(Vector3.Zero, 0, null);
        var brain = car.Brain!;
        var network = brain.Graph.Modules.OfType<NeuralNetworkModule>().Single();
        var snapshot = brain.Capture("przed");
        network.Network.Randomize();

        WhiskerRewiring.SetCount(car, 7);
        brain.Restore(brain.Snapshots.Single());

        Assert.Equal(snapshot.Id, brain.Snapshots.Single().Id);
        Assert.Equal(3 + 7, network.Network.Layers[0]);
        brain.Graph.Validate();
    }

    [Fact]
    public void SetCount_RefusesNetworkWithWhiskersInsideAnExpression_AndChangesNothing()
    {
        var car = WorldObjectCatalog.CreateNeuralCar(Vector3.Zero, 0, null);
        var network = car.Brain!.Graph.Modules.OfType<NeuralNetworkModule>().Single();
        network.Inputs[^1] = new NeuralInput("Ray0 + Ray4");
        car.Brain.Graph.Invalidate();

        var error = Assert.Throws<InvalidOperationException>(() => WhiskerRewiring.SetCount(car, 9));

        Assert.Contains("Ray0 + Ray4", error.Message);
        Assert.Equal(5, WorldObjectCatalog.WhiskerCountOf(car));
        Assert.Equal(8, network.Network.Layers[0]);
    }
}

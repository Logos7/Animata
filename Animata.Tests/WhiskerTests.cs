using System.Numerics;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
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

    [Fact]
    public void WhiskerCounts_AreOddFromOneToMax()
    {
        Assert.Equal("1,3,5,7,9,11,13,15,17,19,21,23,25", string.Join(",", WorldObjectCatalog.WhiskerCounts));
        Assert.Equal(WorldObjectCatalog.MaxWhiskers, WorldObjectCatalog.WhiskerCounts[^1]);
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

    [Fact]
    public void DefaultCar_KeepsFiveWhiskersEvery30Degrees()
    {
        var car = WorldObjectCatalog.CreateControllerCar(Vector3.Zero, 0, null);
        var degrees = car.Body.Sensors.OfType<RaySensor>().Single().Angles.Select(aAngle => (int)MathF.Round(aAngle * 180 / MathF.PI));

        Assert.Equal(WorldObjectCatalog.DefaultWhiskers, WorldObjectCatalog.WhiskerCountOf(car));
        Assert.Equal("-60,-30,0,30,60", string.Join(",", degrees));
        Assert.Equal(90, WorldObjectCatalog.CreateCarNeuralModule().Network.ParameterCount);
    }

    [Theory]
    [MemberData(nameof(ValidCounts))]
    public void Cars_WithAnyValidCount_BuildAndRun(int aCount)
    {
        var world = new World();
        var target = WorldObjectCatalog.CreateTargetBall(new Vector3(6, 0, 0));
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
}

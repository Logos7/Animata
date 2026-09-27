using System.Numerics;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.Sensors;
using Animata.Core.WorldObjects;

namespace Animata.Tests;

public class WiringTests
{
    [Fact]
    public void CarNetwork_WithPortTypo_FailsLoudly()
    {
        var module = WorldObjectCatalog.CreateCarNeuralModule();
        module.Ports.Add("Ray5");            // autko ma Ray0..Ray4
        module.Ports.Add("Directionx");      // literówka

        var typo = Assert.Throws<ArgumentException>(() =>
            WorldObjectCatalog.CreateCar(Vector3.Zero, 0, WorldObjectCatalog.NeuralColor, null, module));
        Assert.Contains("Ray5", typo.Message);
        Assert.Contains("Directionx", typo.Message);

        // Bez literówek ta sama sieć buduje się normalnie.
        WorldObjectCatalog.CreateNeuralCar(Vector3.Zero, 0, null);
    }

    [Fact]
    public void Cylinder_CanReadAnyEyePort()
    {
        var module = new NeuralNetworkModule(new NeuralNetwork(1, 2)) { Name = "Neural" };
        module.Ports.Add(TargetSensor.DistancePort);
        module.Inputs.Add(new NeuralInput(TargetSensor.DistancePort));
        module.Outputs.Add(new NeuralOutput("Turn"));
        module.Outputs.Add(new NeuralOutput("Step"));

        var creature = WorldObjectCatalog.CreateSeeker(Vector3.Zero, WorldObjectCatalog.NeuralColor, null, module);
        Assert.Single(creature.Brain!.Graph.Connections.Where(aLink => aLink.TargetPort == TargetSensor.DistancePort));
    }

    [Fact]
    public void Demo_HasFourWiredCreatures()
    {
        var demo = WorldObjectCatalog.CreateDemo();
        Assert.Equal(4, demo.Creatures.Count);
        foreach (var creature in demo.Creatures)
            creature.Brain!.Graph.Validate();
        demo.World.Update(1f / 30f);
    }
}

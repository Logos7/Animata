using System.Numerics;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Tests;

/// <summary>Edycja kształtu sieci: liczba neuronów w warstwie, dodawanie i usuwanie warstw ukrytych.</summary>
public class NetworkEditingTests
{
    private static NeuralNetwork Network(params int[] aLayers)
    {
        var network = new NeuralNetwork(aLayers);
        network.Randomize(new Random(1));
        return network;
    }

    [Fact]
    public void ResizeLayer_KeepsSurvivingWeights()
    {
        var network = Network(3, 5, 2);
        var before = network.CopyWeights();
        network.ResizeLayer(1, 7);
        Assert.Equal("3,7,2", string.Join(",", network.Layers));
        for (var neuron = 0; neuron < 5; neuron++)
            Assert.Equal(before[0][neuron], network.Weights[0][neuron]);
        for (var neuron = 0; neuron < 2; neuron++)
            Assert.Equal(before[1][neuron], network.Weights[1][neuron][..5]);

        network.ResizeLayer(1, 2);
        Assert.Equal(before[0][1], network.Weights[0][1]);
        Assert.Equal(2, network.Evaluate([0.1f, 0.2f, 0.3f]).Length);
        Assert.Equal(2 * 4 + 2 * 3, network.ParameterCount);
    }

    [Fact]
    public void InsertAndRemoveLayer_KeepOtherMatrices()
    {
        var network = Network(3, 4, 2);
        var before = network.CopyWeights();
        network.InsertLayer(2, 6);
        Assert.Equal("3,4,6,2", string.Join(",", network.Layers));
        Assert.Equal(before[0][3], network.Weights[0][3]);
        Assert.Equal(6, network.Weights[2][0].Length);

        var tail = network.CopyWeights()[2];
        network.InsertLayer(1, 5);
        Assert.Equal("3,5,4,6,2", string.Join(",", network.Layers));
        Assert.Equal(5, network.Weights[1][0].Length);
        Assert.Equal(tail[1], network.Weights[3][1]);

        network.RemoveLayer(1);
        network.RemoveLayer(2);
        Assert.Equal("3,4,2", string.Join(",", network.Layers));
        Assert.Equal(2, network.Evaluate([1, 0, -1]).Length);
        Assert.Equal(2, network.Activations(2).Length);
    }

    [Fact]
    public void ReshapedCar_StillDrives_AndOldSnapshotRestoresOldShape()
    {
        using var world = new World();
        var car = WorldObjectCatalog.CreateNeuralCar(Vector3.Zero, 0, null);
        world.Add(car);
        var module = car.Brain!.Graph.Modules.OfType<NeuralNetworkModule>().Single();
        var shape = string.Join(",", module.Network.Layers);
        var snapshot = car.Brain.Capture("przed");

        module.Network.InsertLayer(1, 9);
        module.Network.ResizeLayer(2, 3);
        car.Brain.Graph.InvalidateDeep();
        car.Brain.Graph.Validate();
        for (var tick = 0; tick < 10; tick++)
            world.Update(1f / 30f);
        Assert.False(car.Brain.Matches(snapshot));

        car.Brain.Restore(snapshot);
        Assert.Equal(shape, string.Join(",", module.Network.Layers));
        Assert.True(car.Brain.Matches(snapshot));
        world.Update(1f / 30f);
    }

}

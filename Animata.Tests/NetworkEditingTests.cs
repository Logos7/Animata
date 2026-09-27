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
    public void InputsAndOutputs_CannotBeEdited()
    {
        var network = Network(3, 4, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => network.ResizeLayer(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => network.ResizeLayer(2, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => network.RemoveLayer(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => network.InsertLayer(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => network.ResizeLayer(1, NeuralNetwork.MaxLayerSize + 1));
        network.RemoveLayer(1);
        Assert.Equal("3,2", string.Join(",", network.Layers));
        for (var layer = 0; layer < NeuralNetwork.MaxHiddenLayers; layer++)
            network.InsertLayer(1, 2);
        Assert.Throws<InvalidOperationException>(() => network.InsertLayer(1, 2));
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

    [Fact]
    public void DeeperNetwork_SurvivesWhiskerChangeAndSaving()
    {
        using var world = new World();
        var car = WorldObjectCatalog.CreateNeuralCar(Vector3.Zero, 0, null, 5);
        world.Add(car);
        var module = car.Brain!.Graph.Modules.OfType<NeuralNetworkModule>().Single();
        module.Network.InsertLayer(2, 6);
        module.Network.RemoveLayer(1);
        module.Network.InsertLayer(1, 3);
        car.Brain.Graph.InvalidateDeep();

        WhiskerRewiring.SetCount(car, 9);
        Assert.Equal(module.Inputs.Count, module.Network.Layers[0]);
        Assert.Equal("3,6", string.Join(",", module.Network.Layers.Skip(1).Take(2)));
        world.Update(1f / 30f);

        var json = Animata.Core.Persistence.WorldFile.ToJson(Animata.Core.Persistence.WorldFile.Capture(world, "t", 0));
        using var restored = Animata.Core.Persistence.WorldFile.Restore(Animata.Core.Persistence.WorldFile.FromJson(json)).World;
        var twin = ((Animata.Core.Entities.ActiveEntity)restored.Find(car.Id)!).Brain!.Graph.Modules.OfType<NeuralNetworkModule>().Single();
        Assert.Equal(string.Join(",", module.Network.Layers), string.Join(",", twin.Network.Layers));
        restored.Update(1f / 30f);
    }
}

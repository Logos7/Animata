using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.WorldObjects;

namespace Animata.Tests;

public class SnapshotTests
{
    [Fact]
    public void EveryStateType_RoundTripsThroughJson()
    {
        ModuleState[] states =
        [
            new ApproachTargetModule().CaptureState(),
            new ConstantModule("V", 0.25f).CaptureState(),
            WorldObjectCatalog.CreateAvoidController().CaptureState(),
            WorldObjectCatalog.CreateCarNeuralModule().CaptureState()
        ];
        foreach (var state in states)
        {
            var json = state.ToJson();
            Assert.Contains("\"$type\"", json);
            var copy = ModuleState.FromJson(json);
            Assert.Equal(state.GetType(), copy.GetType());
            Assert.True(copy.SameAs(state));
        }
    }

    [Fact]
    public void CaptureIfChanged_SkipsIdenticalState()
    {
        var creature = WorldObjectCatalog.CreateNeuralSeeker(default, null);
        var brain = creature.Brain!;
        var network = brain.Graph.Modules.OfType<NeuralNetworkModule>().Single();

        Assert.NotNull(brain.CaptureIfChanged("a", [network]));
        Assert.Null(brain.CaptureIfChanged("b", [network]));
        network.Network.Randomize(new Random(3));
        Assert.NotNull(brain.CaptureIfChanged("c", [network]));
        Assert.Equal(2, brain.Snapshots.Count);
    }

    [Fact]
    public void Restore_CopiesArrays_SoSnapshotStaysIntact()
    {
        var creature = WorldObjectCatalog.CreateNeuralSeeker(default, null);
        var brain = creature.Brain!;
        var network = brain.Graph.Modules.OfType<NeuralNetworkModule>().Single();
        var snapshot = brain.Capture("x", network);

        brain.Restore(snapshot);
        network.Network.Weights[0][0][0] = 42;
        Assert.False(brain.Matches(snapshot));
        brain.Restore(snapshot);
        Assert.True(brain.Matches(snapshot));
    }

    [Fact]
    public void StepBack_SkipsSnapshotsEqualToCurrentState_AndWraps()
    {
        var creature = WorldObjectCatalog.CreateNeuralSeeker(default, null);
        var brain = creature.Brain!;
        var network = brain.Graph.Modules.OfType<NeuralNetworkModule>().Single();
        var history = new SnapshotHistory();

        network.Network.Randomize(new Random(1));
        var a = history.Capture(brain, "A", network);
        network.Network.Randomize(new Random(2));
        var b = history.Capture(brain, "B", network);
        Assert.NotNull(a);
        Assert.NotNull(b);

        // Stan bieżący = B, więc pierwsze Z musi coś zmienić: A.
        Assert.Same(a, history.StepBack(brain)?.Snapshot);
        Assert.Same(b, history.StepBack(brain)?.Snapshot);   // po najstarszym wraca do najnowszego
        Assert.Same(a, history.StepBack(brain)?.Snapshot);

        // Tylko snapshoty identyczne ze stanem → nic do przywrócenia.
        var single = WorldObjectCatalog.CreateNeuralSeeker(default, null).Brain!;
        history.Capture(single, "only");
        Assert.Null(history.StepBack(single));
    }
}

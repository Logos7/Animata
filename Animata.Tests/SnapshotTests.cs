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
    public void SameAs_ComparesArrayContents()
    {
        var module = WorldObjectCatalog.CreateCarNeuralModule();
        var first = module.CaptureState();
        var second = module.CaptureState();
        Assert.NotEqual(first, second);  // Equals rekordu: tablice po referencji
        Assert.True(first.SameAs(second));

        module.Network.Weights[0][0][0] += 0.001f;
        Assert.False(first.SameAs(module.CaptureState()));
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
    public void CaptureIfChanged_KeepsSnapshotWithMoreModules()
    {
        var creature = WorldObjectCatalog.CreateControllerCar(default, 0, null);
        var brain = creature.Brain!;
        var controller = brain.Graph.Modules.OfType<AvoidAndSeekModule>().Single();
        var extra = brain.Graph.Add(new ConstantModule());

        brain.Capture("only controller", controller);
        // Najnowszy nie zawiera stanu `extra`, więc pełny snapshot nie jest duplikatem.
        Assert.NotNull(brain.CaptureIfChanged("all"));
        Assert.Null(brain.CaptureIfChanged("controller again", [controller]));
        Assert.Equal(1, brain.Snapshots[^1].Modules.Count(aEntry => aEntry.ModuleId == extra.Id));
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

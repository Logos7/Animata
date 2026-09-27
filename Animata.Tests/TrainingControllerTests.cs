using System.Diagnostics;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Training;
using Animata.Core.WorldObjects;

namespace Animata.Tests;

public class TrainingControllerTests : IDisposable
{
    private readonly SnapshotHistory _history = new();
    private readonly List<TrainingController> _controllers = [];

    public void Dispose()
    {
        foreach (var controller in _controllers)
            controller.Dispose();
    }

    [Fact]
    public void FinishedTraining_KeepsStartAndChampion()
    {
        var training = Controller(aMaxGenerations: 4);
        var (creature, brain, network) = NeuralCar();
        var start = network.Network.GetParameters();

        Assert.True(training.Start(creature, aSeed: 1));
        Assert.False(training.Start(creature, aSeed: 1)); // już się uczy
        WaitUntilFinished(training);

        Assert.Equal("przed nauką", brain.Snapshots[0].Label);
        Assert.Equal(start, Parameters(brain.Snapshots[0]));
        Assert.InRange(brain.Snapshots.Count, 1, 2);       // mistrz tylko wtedy, gdy różni się od startu
        Assert.True(brain.Matches(brain.Snapshots[^1]));    // w scenie są wagi ostatniego snapshotu
    }

    /// <summary>Z w trakcie nauki: nauka staje, mistrz zostaje w snapshocie, a sieć wraca do stanu sprzed nauki.</summary>
    [Fact]
    public void StepBackDuringTraining_ReturnsToStateBeforeTraining()
    {
        var training = Controller(aMaxGenerations: 1000);
        var (creature, brain, network) = NeuralCar();
        var start = network.Network.GetParameters();
        training.Start(creature, aSeed: 2);
        WaitForChampion(training, brain);

        // Tak robi okno na Z: najpierw stop (mistrz → snapshot), potem krok wstecz.
        training.Stop(brain);
        var champion = network.Network.GetParameters();
        Assert.NotEqual(start, champion);
        Assert.Equal(2, brain.Snapshots.Count);

        var restored = _history.StepBack(brain);
        Assert.Equal("przed nauką", restored?.Snapshot.Label);
        Assert.Equal(start, network.Network.GetParameters());

        // Następne Z zawija na najnowszy — mistrza.
        _history.StepBack(brain);
        Assert.Equal(champion, network.Network.GetParameters());
    }

    [Fact]
    public void RepeatedStopStart_NeverStoresTheSameStateTwiceInARow()
    {
        var training = Controller(aMaxGenerations: 4);
        var (creature, brain, _) = NeuralCar();
        for (var round = 0; round < 3; round++)
        {
            training.Start(creature, aSeed: 3);
            training.Stop(brain);
        }

        AssertNoConsecutiveDuplicates(brain);
    }

    [Fact]
    public void StopWithoutAnyGeneration_AddsOnlyTheStartSnapshot()
    {
        var training = Controller(aMaxGenerations: 0);
        var (creature, brain, _) = NeuralCar();
        for (var round = 0; round < 3; round++)
        {
            training.Start(creature, aSeed: 3);
            WaitUntilFinished(training);
        }

        Assert.Single(brain.Snapshots);
    }

    [Fact]
    public void RandomizeAndRestart_KeepsOldWeights_WithoutDuplicates()
    {
        var training = Controller(aMaxGenerations: 4);
        var (creature, brain, network) = NeuralCar();
        var original = network.Network.GetParameters();
        training.Start(creature, aSeed: 4);
        training.RandomizeAndRestart(creature, aSeed: 5);
        training.Stop(brain);

        Assert.NotEqual(original, network.Network.GetParameters());
        Assert.True(brain.Snapshots.Any(aSnapshot => Parameters(aSnapshot).SequenceEqual(original)));
        AssertNoConsecutiveDuplicates(brain);
    }

    private TrainingController Controller(int aMaxGenerations)
    {
        var controller = new TrainingController(_history, aMaxGenerations, TestWorlds.Quick);
        _controllers.Add(controller);
        return controller;
    }

    private static (ActiveEntity Creature, Brain Brain, NeuralNetworkModule Network) NeuralCar()
    {
        var creature = WorldObjectCatalog.CreateNeuralCar(default, 0, null);
        return (creature, creature.Brain!, TrainingController.FindNetwork(creature)!);
    }

    private static float[] Parameters(BrainSnapshot aSnapshot)
    {
        var module = WorldObjectCatalog.CreateCarNeuralModule();
        module.RestoreState(aSnapshot.Modules.Single().State);
        return module.Network.GetParameters();
    }

    private static void AssertNoConsecutiveDuplicates(Brain aBrain)
    {
        for (var index = 1; index < aBrain.Snapshots.Count; index++)
            Assert.False(aBrain.Snapshots[index].Modules.Single().State.SameAs(aBrain.Snapshots[index - 1].Modules.Single().State),
                $"snapshots {index - 1} and {index} are identical");
    }

    private static void WaitUntilFinished(TrainingController aTraining)
    {
        var clock = Stopwatch.StartNew();
        while (aTraining.Count > 0)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), "training did not finish");
            aTraining.Poll(out var error);
            Assert.Null(error);
            Thread.Sleep(5);
        }
    }

    private static void WaitForChampion(TrainingController aTraining, Brain aBrain)
    {
        var clock = Stopwatch.StartNew();
        while (aTraining.ProgressOf(aBrain) is not { ChampionGeneration: > 0 })
        {
            Assert.True(aTraining.IsTraining(aBrain), "training ended without a better champion");
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), "no champion in time");
            aTraining.Poll(out _);
            Thread.Sleep(5);
        }
    }
}

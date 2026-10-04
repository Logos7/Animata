using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Bodies;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.Entities;
using Animata.Core.Persistence;
using Animata.Core.Physics;
using Animata.Core.Sensors;
using Animata.Core.Training;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Tests;

public class AuditRegressionTests
{
    [Fact]
    public void CmaEs_BestBelongsToTheCurrentGeneration()
    {
        var optimizer = new CmaEs([0f, 0f], new EvolutionOptions { PopulationSize = 4, Seed = 3 });
        optimizer.NextGeneration((_, _) => 100);
        optimizer.NextGeneration((_, _) => -10);
        Assert.Equal(-10, optimizer.BestFitness);
        Assert.Equal(2, optimizer.Generation);
    }

    [Fact]
    public async Task Stop_DetachesWithoutWaiting_AndDoesNotPublishLateProgress()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var optimizer = new Evolution([0f], new EvolutionOptions { PopulationSize = 4, EliteCount = 1, MaxParallelism = 1 });
        await using var trainer = new BackgroundTrainer(optimizer, (_, _, _) =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return 1;
        }, 1);
        trainer.Start();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            await Task.Run(trainer.Stop).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(trainer.IsRunning);
            Assert.False(trainer.Completion.IsCompleted);
        }
        finally
        {
            release.Set();
        }
        await trainer.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        var version = 0;
        Assert.False(trainer.TryGetProgress(ref version, out _));
        Assert.Null(trainer.Error);
    }

    [Fact]
    public async Task TokenAwareFitness_IsCancelledAndReleasesTheScheduler()
    {
        using var entered = new ManualResetEventSlim();
        var optimizer = new Evolution([0f], new EvolutionOptions { PopulationSize = 4, EliteCount = 1, MaxParallelism = 1 });
        await using var trainer = new BackgroundTrainer(optimizer, (_, _, aCancellation) =>
        {
            entered.Set();
            aCancellation.WaitHandle.WaitOne();
            aCancellation.ThrowIfCancellationRequested();
            return 0;
        });
        trainer.Start();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        await trainer.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(trainer.Error);
        optimizer.NextGeneration((_, _) => 1);
        Assert.Equal(1, optimizer.BestFitness);
    }

    [Fact]
    public void TwoEyesAndTwoDrives_GetIndependentQualifiedConnections()
    {
        var creature = (Creature)Roller.Design.Type.Create();
        creature.Body.Sensors.Clear();
        creature.Body.Sensors.Add(new TargetSensor { Slot = "Left" });
        creature.Body.Sensors.Add(new TargetSensor { Slot = "Right" });
        creature.Body.Actuators.Clear();
        creature.Body.Actuators.Add(new DiskDriveActuator { Slot = "Front" });
        creature.Body.Actuators.Add(new DiskDriveActuator { Slot = "Back" });
        var network = CreatureDesign.GeneralNetwork(creature);
        WorldObjectCatalog.BuildBrain(creature.Brain!, network);
        creature.Brain!.Graph.Validate();
        Assert.Contains("Left.Found", network.Ports);
        Assert.Contains("Right.Found", network.Ports);
        Assert.Equal(creature.Body.Sensors.Sum(aSensor => aSensor.OutputPorts.Count), network.Ports.Count);
        Assert.Equal(creature.Body.Actuators.Sum(aDrive => aDrive.InputPorts.Count), network.Outputs.Count);
        foreach (var link in creature.Brain.Graph.Connections.Where(aLink => aLink.TargetId == network.Id))
        {
            var sensor = Assert.IsType<SensorModule>(creature.Brain.Graph.Find(link.SourceId));
            Assert.Equal(sensor.Slot + "." + link.SourcePort, link.TargetPort);
        }
        foreach (var link in creature.Brain.Graph.Connections.Where(aLink => aLink.SourceId == network.Id))
        {
            var drive = Assert.IsType<ActuatorModule>(creature.Brain.Graph.Find(link.TargetId));
            Assert.Equal(drive.Slot + "." + link.TargetPort, link.SourcePort);
        }
        var expression = SensorExpression.Compile("Left.Found - Right.Found");
        Assert.Equal(1, expression.Evaluate(new Dictionary<string, float> { ["Left.Found"] = 1, ["Right.Found"] = 0 }));
    }

    [Fact]
    public void ReplacingASensorWithoutChangingCount_RebindsOnThink()
    {
        var creature = WorldObjectCatalog.CreateNeuralCar(default, 0, null);
        var index = creature.Body.Sensors.Select((aSensor, aIndex) => (aSensor, aIndex)).Single(aPair => aPair.aSensor is TargetSensor).aIndex;
        var previous = creature.Body.Sensors[index];
        var replacement = new TargetSensor { Slot = previous.Slot };
        creature.Body.Sensors[index] = replacement;
        using var world = new World();
        creature.Brain!.Think(creature, world, TestWorlds.Delta);
        Assert.Same(replacement, creature.Brain.Graph.Modules.OfType<SensorModule>().Single(aModule => aModule.Slot == replacement.Slot).Sensor);
    }

    [Fact]
    public void ConfigurationCheck_IgnoresWeightsButDetectsExpressionChanges()
    {
        var module = WorldObjectCatalog.CreateCarNeuralModule();
        var matches = module.CaptureConfigurationCheck();
        module.Randomize(new Random(7));
        Assert.True(matches());
        module.Inputs[0].Expression += " * 0.5";
        Assert.False(matches());
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void BodyPlan_RejectsNonFiniteMass(float aMass)
    {
        Assert.Throws<ArgumentException>(() => new BodyPlanBuilder().Part("Root", PartShape.Sphere, Vector3.One, aMass, Vector3.Zero).Build());
    }

    [Fact]
    public void BodyPlan_RejectsZeroQuaternion()
    {
        Assert.Throws<ArgumentException>(() => new BodyPlanBuilder().Part("Root", PartShape.Sphere, Vector3.One, 1, Vector3.Zero, new Quaternion()).Build());
    }

    [Fact]
    public void InvalidParameters_DoNotPartiallyModifyTheNetwork()
    {
        var network = new NeuralNetwork([2, 2, 1]);
        network.Randomize(new Random(5));
        var original = network.GetParameters();
        var invalid = original.Select(aValue => aValue + 1).ToArray();
        invalid[^1] = float.NaN;
        Assert.Throws<ArgumentException>(() => network.SetParameters(invalid));
        Assert.Equal(original, network.GetParameters());
    }

    [Fact]
    public void UnchangedStaticShapes_DoNotAllocatePerTick()
    {
        using var world = TestWorlds.Floor(10);
        world.Add(TestWorlds.BareSnake(3));
        var box = (IPhysicalEntity)world.Entities.OfType<Box>().Single();
        for (var index = 0; index < 1000; index++)
            box.BeforePhysicsStep(world.Physics!, TestWorlds.Delta);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10000; index++)
            box.BeforePhysicsStep(world.Physics!, TestWorlds.Delta);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void AmbiguousBareSensorPort_IsRejected()
    {
        var creature = (Creature)Roller.Design.Type.Create();
        creature.Body.Sensors.Add(new TargetSensor { Slot = "OtherEye" });
        Assert.Throws<ArgumentException>(() => WorldObjectCatalog.BuildBrain(creature.Brain!, new ApproachTargetModule()));
    }

    [Fact]
    public void CancelledEpisode_DoesNotCreateAWorld()
    {
        var prepared = false;
        var rig = SeekRigs.Car with { PrepareWorld = _ => prepared = true };
        Assert.Throws<OperationCanceledException>(() => SeekTargetTask.Run(new ApproachTargetModule(),
            [TestWorlds.OpenRoad()], rig.DefaultOptions, rig, new CancellationToken(true)));
        Assert.False(prepared);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidTimestep_IsRejected(float aDelta)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SeekTargetTask.Run(new ApproachTargetModule(),
            [TestWorlds.OpenRoad()], SeekRigs.Car.DefaultOptions with { Delta = aDelta }, SeekRigs.Car));
    }

    [Fact]
    public void BentCopies_OffsetEveryPartAndPreserveTheSource()
    {
        using var world = WorldObjectCatalog.CreateSnakeScene().World;
        var snake = world.Entities.OfDesign(Snake.Design).First();
        var document = WorldFile.CaptureEntities([snake]).Single();
        var parts = snake.PartPositions.SelectMany((aPosition, aIndex) =>
        {
            var rotation = snake.PartOrientations[aIndex];
            return new[] { aPosition.X, aPosition.Y + aIndex * 0.1f, aPosition.Z, rotation.X, rotation.Y, rotation.Z, rotation.W };
        }).ToArray();
        document = document with { Parts = parts };
        var original = (float[])parts.Clone();
        var offset = new Vector3(3, -2, 1);
        var copy = Assert.IsType<Creature>(WorldFile.RestoreCopies([document], offset).Single());
        for (var index = 0; index < copy.PartPositions.Count; index++)
            Assert.Equal(new Vector3(parts[index * 7], parts[index * 7 + 1], parts[index * 7 + 2]) + offset, copy.PartPositions[index]);
        Assert.Equal(original, document.Parts);
    }
}

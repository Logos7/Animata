using System.Numerics;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Persistence;
using Animata.Core.Sensors;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Tests;

/// <summary>Zapis i odczyt światów (JSON) — z mózgami, snapshotami i podgrafami.</summary>
public class PersistenceTests
{
    private const float Delta = 1f / 30f;

    private static string RoundTrip(World aWorld, out World aRestored)
    {
        var json = WorldFile.ToJson(WorldFile.Capture(aWorld, "test"));
        aRestored = WorldFile.Restore(WorldFile.FromJson(json)).World;
        return json;
    }

    [Fact]
    public void Demo_SavesAndLoadsToTheSameJson()
    {
        using var world = WorldObjectCatalog.CreateDemo().World;
        var json = RoundTrip(world, out var restored);
        using (restored)
            Assert.Equal(json, WorldFile.ToJson(WorldFile.Capture(restored, "test")));
    }

    [Fact]
    public void Demo_AfterLoading_SimulatesExactlyLikeTheOriginal()
    {
        using var world = WorldObjectCatalog.CreateDemo().World;
        RoundTrip(world, out var restored);
        using (restored)
        {
            for (var tick = 0; tick < 90; tick++)
            {
                world.Update(Delta);
                restored.Update(Delta);
            }
            foreach (var entity in world.Entities)
            {
                var twin = restored.Find(entity.Id);
                Assert.NotNull(twin);
                Assert.Equal(entity.Body.Position, twin.Body.Position);
                Assert.Equal(entity.Body.Rotation, twin.Body.Rotation);
            }
        }
    }

    [Fact]
    public void SimulationTime_SurvivesSaveAndLoad_SoTheClockContinues()
    {
        using var world = WorldObjectCatalog.CreateSnakeScene().World;
        for (var tick = 0; tick < 45; tick++)
            world.Update(Delta);
        RoundTrip(world, out var restored);
        using (restored)
        {
            Assert.Equal(world.Time, restored.Time);
            var snake = world.Entities.OfType<SnakeCreature>().First();
            var clock = snake.Body.Sensors.OfType<ClockSensor>().Single();
            var twinClock = restored.Find(snake.Id)!.Body.Sensors.OfType<ClockSensor>().Single();
            Assert.Equal(clock.Read(snake, world)[ClockSensor.SinPort], twinClock.Read(restored.Find(snake.Id)!, restored)[ClockSensor.SinPort]);
        }
    }

    [Fact]
    public void SnakeScene_SavesAndLoads_WithCpgAndSnapshots()
    {
        using var world = WorldObjectCatalog.CreateSnakeScene().World;
        var snake = world.Entities.OfType<SnakeCreature>().Single(aSnake => aSnake.Name == "Wąż CPG");
        snake.SetSegments(11);
        var neural = world.Entities.OfType<SnakeCreature>().Single(aSnake => aSnake.Name == "Wąż NN");
        neural.SetSegments(5);
        var json = RoundTrip(world, out var restored);
        using (restored)
        {
            Assert.Equal(json, WorldFile.ToJson(WorldFile.Capture(restored, "test")));
            Assert.Equal(5, restored.Entities.OfType<Box>().Count(aBox => !aBox.Locked));
            var neuralTwin = (SnakeCreature)restored.Find(neural.Id)!;
            Assert.Equal(5, neuralTwin.Segments);
            Assert.True(neural.Brain!.Graph.Modules.OfType<NeuralNetworkModule>().Single().CaptureState()!
                .SameAs(neuralTwin.Brain!.Graph.Modules.OfType<NeuralNetworkModule>().Single().CaptureState()));
            var twin = (SnakeCreature)restored.Find(snake.Id)!;
            Assert.Equal(11, twin.Segments);
            Assert.NotNull(restored.Physics);
            Assert.Contains(twin.Brain!.Snapshots, aSnapshot => aSnapshot.Label == "ręczne parametry");
            var target = restored.Entities.OfType<Sphere>().Single();
            Assert.Equal(target.Id, twin.Body.Sensors.OfType<TargetSensor>().Single().TargetId);
            Assert.True(target.Body.Position.Z > 0.05f);
            restored.Update(Delta);
        }
    }

    [Fact]
    public void Brain_WithSubgraphRouterAndConstant_SurvivesSaving()
    {
        using var world = new World();
        var car = WorldObjectCatalog.CreateNeuralCar(Vector3.Zero, 0, null, 9);
        world.Add(car);
        var graph = car.Brain!.Graph;
        var network = graph.Modules.OfType<NeuralNetworkModule>().Single();
        var group = BrainGraphEditing.Group(graph, [network]);
        group.Inner.Add(new ConstantModule("Bias", 0.25f) { Name = "Stała" });
        graph.Add(new RouterModule(2, "A", "B") { Name = "Router" });
        graph.Positions[group.Id] = new Vector2(120, 40);
        car.Brain.Capture("po grupowaniu");
        graph.Validate();

        var json = RoundTrip(world, out var restored);
        using (restored)
        {
            Assert.Equal(json, WorldFile.ToJson(WorldFile.Capture(restored, "test")));
            var twin = (CarCreature)restored.Find(car.Id)!;
            Assert.Equal(9, WorldObjectCatalog.WhiskerCountOf(twin));
            var twinGroup = twin.Brain!.Graph.Modules.OfType<CompositeModule>().Single();
            Assert.Equal(group.Id, twinGroup.Id);
            Assert.Equal(new Vector2(120, 40), twin.Brain.Graph.Positions[group.Id]);
            var twinNetwork = twinGroup.Inner.Modules.OfType<NeuralNetworkModule>().Single();
            Assert.True(network.CaptureState()!.SameAs(twinNetwork.CaptureState()));
            Assert.True(twin.Brain.Matches(twin.Brain.Snapshots[^1]));
            restored.Update(Delta);
        }
    }

    [Fact]
    public void DriveSettings_SurviveSaving()
    {
        using var world = WorldObjectCatalog.CreateDemo().World;
        var car = world.Entities.OfType<CarCreature>().First();
        var steering = car.Body.Actuators.OfType<Animata.Core.Actuators.SteeringDriveActuator>().Single();
        steering.MaxSpeed = 4;
        steering.MaxSteerAngle = 0.5f;
        steering.DriveTorque = 7;
        var cylinder = world.Entities.OfType<CylinderCreature>().First();
        cylinder.Body.Actuators.OfType<Animata.Core.Actuators.DiskDriveActuator>().Single().MaxTurnSpeed = 1.25f;

        RoundTrip(world, out var restored);
        using (restored)
        {
            var twin = restored.Find(car.Id)!.Body.Actuators.OfType<Animata.Core.Actuators.SteeringDriveActuator>().Single();
            Assert.Equal(4f, twin.MaxSpeed);
            Assert.Equal(0.5f, twin.MaxSteerAngle);
            Assert.Equal(7f, twin.DriveTorque);
            Assert.Equal(1.25f, restored.Find(cylinder.Id)!.Body.Actuators.OfType<Animata.Core.Actuators.DiskDriveActuator>().Single().MaxTurnSpeed);
        }
    }

    [Fact]
    public void File_PointsAtCurrentSnapshot_AndLoadingStartsFromIt()
    {
        using var world = WorldObjectCatalog.CreateSnakeScene().World;
        var snake = world.Entities.OfType<SnakeCreature>().Single(aSnake => aSnake.Name == "Wąż CPG");
        var cpg = snake.Brain!.Graph.Modules.OfType<CpgModule>().Single();
        var random = snake.Brain.Capture("losowe", cpg);
        var manual = snake.Brain.Snapshots.Single(aSnapshot => aSnapshot.Label == "ręczne parametry");
        snake.Brain.Restore(manual);
        Assert.Same(manual, snake.Brain.CurrentSnapshot());

        // Plik wskazuje „ręczne parametry”, choć zapisany stan modułu podmieniamy na losowy — wygrywa snapshot.
        var document = WorldFile.Capture(world, "test");
        var snakeDocument = document.Entities.OfType<SnakeDocument>().Single(aDocument => aDocument.Id == snake.Id);
        Assert.Equal(manual.Id, snakeDocument.Brain.Current);
        var randomState = random.Modules.Single().State;
        var modules = snakeDocument.Brain.Modules
            .Select(aModule => aModule.Id == cpg.Id ? new StateNode(aModule.Id, aModule.Name, randomState) : aModule).ToList();
        var edited = document with
        {
            Entities = [.. document.Entities.Select(aEntity => aEntity == snakeDocument
                ? snakeDocument with { Brain = snakeDocument.Brain with { Modules = modules } } : aEntity)]
        };

        using var restored = WorldFile.Restore(WorldFile.FromJson(WorldFile.ToJson(edited))).World;
        var twin = (SnakeCreature)restored.Find(snake.Id)!;
        Assert.Equal(manual.Id, twin.Brain!.CurrentSnapshot()!.Id);
        Assert.True(twin.Brain.Graph.Modules.OfType<CpgModule>().Single().CaptureState()!.SameAs(manual.Modules.Single().State));
    }

    [Fact]
    public void Copies_GetNewIds_OffsetPositions_AndFollowCopiedTargets()
    {
        using var world = WorldObjectCatalog.CreateSnakeScene().World;
        var neural = world.Entities.OfType<SnakeCreature>().Single(aSnake => aSnake.Name == "Wąż NN");
        var cpg = world.Entities.OfType<SnakeCreature>().Single(aSnake => aSnake.Name == "Wąż CPG");
        var target = world.Entities.OfType<Sphere>().Single();
        var slab = world.Entities.OfType<Box>().First(aBox => !aBox.Locked);

        var documents = WorldFile.CaptureEntities([neural, target, slab, cpg]);
        var offset = new Vector3(2, -1, 0);
        var copies = WorldFile.RestoreCopies(documents, offset);
        Assert.Equal(4, copies.Count);
        Assert.All(copies, aCopy => Assert.Null(world.Find(aCopy.Id)));
        foreach (var copy in copies)
            world.Add(copy);

        var neuralCopy = (SnakeCreature)copies[0];
        var targetCopy = (Sphere)copies[1];
        Assert.Equal(neural.Body.Position + offset, neuralCopy.Body.Position);
        Assert.Equal(target.Body.Position + offset, targetCopy.Body.Position);
        Assert.Equal(targetCopy.Id, neuralCopy.Body.Sensors.OfType<TargetSensor>().Single().TargetId);
        Assert.Equal(slab.Size, ((Box)copies[2]).Size);
        Assert.True(neural.Brain!.Graph.Modules.OfType<NeuralNetworkModule>().Single().CaptureState()!
            .SameAs(neuralCopy.Brain!.Graph.Modules.OfType<NeuralNetworkModule>().Single().CaptureState()));
        Assert.Equal(cpg.Brain!.Snapshots.Count, ((SnakeCreature)copies[3]).Brain!.Snapshots.Count);

        // Kopia sama (bez kulki) poluje na tę samą kulkę co oryginał.
        var alone = WorldFile.RestoreCopies(WorldFile.CaptureEntities([neural]), Vector3.UnitX);
        Assert.Equal(target.Id, alone[0].Body.Sensors.OfType<TargetSensor>().Single().TargetId);
        world.Add(alone[0]);
        world.Update(Delta);
    }

}

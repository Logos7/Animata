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
        var json = WorldFile.ToJson(WorldFile.Capture(aWorld, "test", 1.5));
        aRestored = WorldFile.Restore(WorldFile.FromJson(json)).World;
        return json;
    }

    [Fact]
    public void Demo_SavesAndLoadsToTheSameJson()
    {
        using var world = WorldObjectCatalog.CreateDemo().World;
        var json = RoundTrip(world, out var restored);
        using (restored)
            Assert.Equal(json, WorldFile.ToJson(WorldFile.Capture(restored, "test", 1.5)));
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
    public void SnakeScene_SavesAndLoads_WithCpgAndSnapshots()
    {
        using var world = WorldObjectCatalog.CreateSnakeScene().World;
        var snake = world.Entities.OfType<SnakeCreature>().Single();
        snake.SetSegments(11);
        var json = RoundTrip(world, out var restored);
        using (restored)
        {
            Assert.Equal(json, WorldFile.ToJson(WorldFile.Capture(restored, "test", 1.5)));
            var twin = restored.Entities.OfType<SnakeCreature>().Single();
            Assert.Equal(11, twin.Segments);
            Assert.NotNull(restored.Physics);
            Assert.Contains(twin.Brain!.Snapshots, aSnapshot => aSnapshot.Label == "ręczne parametry");
            var target = restored.Entities.OfType<TargetBall>().Single();
            Assert.Equal(target.Id, twin.Body.Sensors.OfType<TargetSensor>().Single().TargetId);
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
            Assert.Equal(json, WorldFile.ToJson(WorldFile.Capture(restored, "test", 1.5)));
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
    public void NewerFormat_IsRejected()
    {
        var json = WorldFile.ToJson(new WorldDocument(WorldFile.Format + 1, "przyszłość", 0, []));
        Assert.Throws<NotSupportedException>(() => WorldFile.FromJson(json));
    }

    [Fact]
    public void SensorModules_BindToSlotsByName()
    {
        var car = WorldObjectCatalog.CreateControllerCar(Vector3.Zero, 0, null);
        var slots = car.Brain!.Graph.Modules.OfType<SensorModule>().Select(aModule => aModule.Slot).ToArray();
        Assert.Equal("Eye,Whiskers", string.Join(",", slots));
        Assert.Equal("Wheels", car.Brain.Graph.Modules.OfType<ActuatorModule>().Single().Slot);
        Assert.Same(car.Body.FindSensor("Whiskers"), car.Body.Sensors.OfType<RaySensor>().Single());
    }
}

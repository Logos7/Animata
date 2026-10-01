using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Bodies;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Sensors;
using Animata.Core.Training;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Tests;

/// <summary>Podłoga, ciała z klocków w fizyce Bepu, wąż z CPG.</summary>
public class SnakeTests
{
    private const float Delta = 1f / 30f;

    private static World FloorWorld()
    {
        var world = new World();
        world.Add(WorldObjectCatalog.CreateFloor(40, 40));
        return world;
    }

    // ---------- podłoga ----------

    [Fact]
    public void Floor_IsALockedBox_AndInvisibleToWhiskers()
    {
        using var world = new World();
        var floor = WorldObjectCatalog.CreateFloor(10, 10);
        world.Add(floor);
        var car = WorldObjectCatalog.CreateControllerCar(Vector3.Zero, 0, null);
        world.Add(car);

        floor.Place(new Vector3(3, 3, 3), Quaternion.Identity);
        world.Update(Delta);

        Assert.True(floor.Locked);
        Assert.Equal(new Vector3(0, 0, -0.2f), floor.Body.Position);
        Assert.Equal(0f, floor.Top, 5);
        Assert.Equal(0, floor.BoundingRadius);
        Assert.NotNull(world.Physics);
        Assert.All(car.Body.Sensors.OfType<RaySensor>().Single().LastDistances, aDistance => Assert.Equal(Car.WhiskerRange, aDistance));
    }

    // ---------- ciała z klocków ----------

    [Fact]
    public void BodyPlanBuilder_RejectsBrokenPlans()
    {
        var capsule = new Vector3(0.1f, 0.2f, 0);
        Assert.Throws<ArgumentException>(() => new BodyPlanBuilder().Build());
        Assert.Throws<ArgumentException>(() => new BodyPlanBuilder()
            .Part("A", PartShape.Capsule, capsule, 1, Vector3.Zero)
            .Part("A", PartShape.Capsule, capsule, 1, Vector3.UnitX).Build());
        Assert.Throws<ArgumentException>(() => new BodyPlanBuilder()
            .Part("A", PartShape.Capsule, capsule, 1, Vector3.Zero)
            .Part("B", PartShape.Capsule, capsule, 1, Vector3.UnitX).Build());
        Assert.Throws<ArgumentException>(() => new BodyPlanBuilder()
            .Part("A", PartShape.Capsule, capsule, 1, Vector3.Zero)
            .Part("B", PartShape.Capsule, capsule, 1, Vector3.UnitX)
            .Joint("J", "B", "A", Vector3.Zero, 1, 1, 1).Build());

        var plan = new BodyPlanBuilder()
            .Part("A", PartShape.Box, new Vector3(0.4f, 0.2f, 0.1f), 1, new Vector3(0, 0, 0.3f))
            .Part("B", PartShape.Capsule, capsule, 1, new Vector3(0.5f, 0, 0.3f))
            .Joint("J", "A", "B", new Vector3(0.25f, 0, 0.3f), 1, 1, 1).Build();
        Assert.Equal("A", plan.Root.Name);
        Assert.Equal(1, plan.IndexOf("B"));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(24)]
    public void SnakePlan_HasSegmentsAndJoints(int aSegments)
    {
        var plan = Snake.DefaultPlan(aSegments);
        Assert.Equal(aSegments, plan.Parts.Count);
        Assert.Equal(aSegments - 1, plan.Joints.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => Snake.DefaultPlan(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Snake.DefaultPlan(Snake.MaxSegments + 1));
    }

    [Fact]
    public void Snake_CreatesPhysics_AndLiesOnTheFloor()
    {
        using var world = FloorWorld();
        var snake = WorldObjectCatalog.CreateSnake(Vector3.Zero, 0, WorldObjectCatalog.RandomColor(), null, WorldObjectCatalog.CreateCpg());
        world.Add(snake);
        Assert.NotNull(world.Physics);

        for (var tick = 0; tick < 90; tick++)
            world.Update(Delta);

        foreach (var position in snake.PartPositions)
        {
            Assert.True(float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z));
            Assert.InRange(position.Z, Snake.SegmentRadius - 0.03f, Snake.SegmentRadius + 0.2f);
        }
    }

    [Fact]
    public void Joints_FollowSpineCommands()
    {
        using var world = FloorWorld();
        var snake = WorldObjectCatalog.CreateSnake(Vector3.Zero, 0, WorldObjectCatalog.RandomColor(), null, WorldObjectCatalog.CreateCpg(4), 4);
        world.Add(snake);
        // Zamiast CPG stała: staw 0 skręcony o połowę zakresu, reszta prosto.
        var graph = snake.Brain!.Graph;
        graph.Remove(graph.Modules.OfType<CpgModule>().Single());
        var yaw = graph.Add(new ConstantModule(JointPorts.Yaw(0), 0.5f));
        graph.Connect(yaw, yaw.Port, graph.Modules.OfType<ActuatorModule>().Single(), yaw.Port);

        for (var tick = 0; tick < 60; tick++)
            world.Update(Delta);

        var expected = 0.5f * snake.Plan.Joints[0].MaxYaw;
        Assert.InRange(MathF.Abs(snake.JointYaw(0)), expected * 0.7f, expected * 1.3f);
        Assert.InRange(MathF.Abs(snake.JointYaw(1)), 0, 0.15f);
        var sense = snake.Body.Sensors.OfType<JointSensor>().Single().Read(snake, world);
        Assert.InRange(MathF.Abs(sense[JointPorts.Yaw(0)]), 0.35f, 0.65f);
    }

    [Fact]
    [Trait(KnownFailures.Trait, KnownFailures.BepuBeta29)]
    public void HandCpg_CrawlsTowardsTheTarget()
    {
        using var world = FloorWorld();
        var target = WorldObjectCatalog.CreateSphere(new Vector3(6, 0, 0));
        world.Add(target);
        var snake = WorldObjectCatalog.CreateSnake(Vector3.Zero, 0, WorldObjectCatalog.RandomColor(), target.Id, WorldObjectCatalog.CreateCpg());
        world.Add(snake);
        var start = Vector3.Distance(snake.PartPositions[0], target.Body.Position);

        for (var tick = 0; tick < 15 * 30; tick++)
            world.Update(Delta);

        var end = Vector3.Distance(snake.PartPositions[0], target.Body.Position);
        Assert.True(end < 1.2f, $"start {start:0.00} m, end {end:0.00} m");
    }

    [Fact]
    public void HandCpg_TurnsTowardsATargetOnTheSide()
    {
        using var world = FloorWorld();
        var target = WorldObjectCatalog.CreateSphere(new Vector3(0, 5, 0));
        world.Add(target);
        var snake = WorldObjectCatalog.CreateSnake(Vector3.Zero, 0, WorldObjectCatalog.RandomColor(), target.Id, WorldObjectCatalog.CreateCpg());
        world.Add(snake);

        for (var tick = 0; tick < 12 * 30; tick++)
            world.Update(Delta);

        var end = Vector3.Distance(snake.PartPositions[0], target.Body.Position);
        Assert.True(end < 1.5f, $"end {end:0.00} m");
    }

    [Fact]
    public void SetSegments_RebuildsBodyAndBrainInPlace()
    {
        using var world = FloorWorld();
        var snake = WorldObjectCatalog.CreateLearningSnake(Vector3.Zero, 0, null);
        world.Add(snake);
        var cpg = snake.Brain!.Graph.Modules.OfType<CpgModule>().Single();
        var parameters = cpg.GetParameters();
        world.Update(Delta);

        Snake.SetSegments(snake, 12);

        Assert.Equal(12, Snake.Segments(snake));
        Assert.Equal(12, snake.PartPositions.Count);
        Assert.Equal(2 * 11, snake.Body.Actuators.OfType<SpineActuator>().Single().InputPorts.Count);
        Assert.Equal(2 * 11, cpg.OutputPorts.Count);
        Assert.True(parameters.SequenceEqual(cpg.GetParameters()));
        var spine = snake.Brain.Graph.Modules.OfType<ActuatorModule>().Single();
        Assert.Equal(2 * 11, snake.Brain.Graph.Connections.Count(aLink => aLink.SourceId == cpg.Id && aLink.TargetId == spine.Id));
        snake.Brain.Graph.Validate();
        for (var tick = 0; tick < 10; tick++)
            world.Update(Delta);

        Snake.SetSegments(snake, 3);
        Assert.Equal(2 * 2, snake.Brain.Graph.Connections.Count(aLink => aLink.SourceId == cpg.Id && aLink.TargetId == spine.Id));
        world.Update(Delta);
        Assert.Same(SeekRigs.SnakeWith(3), SeekRigs.For(snake));
    }

}

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
        world.Add(Floor.At(40, 40));
        return world;
    }

    // ---------- podłoga ----------

    [Fact]
    public void Floor_IsFixed_InvisibleToWhiskers_AndIgnoredByCircleCollisions()
    {
        using var world = new World();
        var floor = Floor.At(10, 10);
        world.Add(floor);
        var car = WorldObjectCatalog.CreateControllerCar(Vector3.Zero, 0, null);
        world.Add(car);

        floor.Place(new Vector3(3, 3, 3), Quaternion.Identity);
        world.Update(Delta);

        Assert.True(floor.IsFixed);
        Assert.Equal(new Vector3(0, 0, -0.1f), floor.Body.Position);
        Assert.Equal(0f, floor.Top, 5);
        Assert.Equal(0, floor.BoundingRadius);
        Assert.NotNull(world.Physics);
        Assert.All(car.Body.Sensors.OfType<RaySensor>().Single().LastDistances, aDistance => Assert.Equal(WorldObjectCatalog.WhiskerRange, aDistance));
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
        var plan = WorldObjectCatalog.SnakePlan(aSegments);
        Assert.Equal(aSegments, plan.Parts.Count);
        Assert.Equal(aSegments - 1, plan.Joints.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldObjectCatalog.SnakePlan(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldObjectCatalog.SnakePlan(WorldObjectCatalog.MaxSnakeSegments + 1));
    }

    [Fact]
    public void Snake_CreatesPhysics_AndLiesOnTheFloor()
    {
        using var world = FloorWorld();
        var snake = WorldObjectCatalog.CreateSnake(Vector3.Zero, 0, WorldObjectCatalog.NeuralColor, null, WorldObjectCatalog.CreateCpg());
        world.Add(snake);
        Assert.NotNull(world.Physics);

        for (var tick = 0; tick < 90; tick++)
            world.Update(Delta);

        foreach (var position in snake.PartPositions)
        {
            Assert.True(float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z));
            Assert.InRange(position.Z, WorldObjectCatalog.SnakeRadius - 0.03f, WorldObjectCatalog.SnakeRadius + 0.2f);
        }
    }

    [Fact]
    public void Snake_WithoutFloor_Falls()
    {
        using var world = new World();
        var snake = WorldObjectCatalog.CreateSnake(Vector3.Zero, 0, WorldObjectCatalog.NeuralColor, null, WorldObjectCatalog.CreateCpg());
        world.Add(snake);
        for (var tick = 0; tick < 30; tick++)
            world.Update(Delta);
        Assert.True(snake.PartPositions[0].Z < -1, $"z = {snake.PartPositions[0].Z}");
    }

    [Fact]
    public void Place_MovesTheWholeBodyAndStopsIt()
    {
        using var world = FloorWorld();
        var snake = WorldObjectCatalog.CreateSnake(Vector3.Zero, 0, WorldObjectCatalog.NeuralColor, null, WorldObjectCatalog.CreateCpg());
        world.Add(snake);
        for (var tick = 0; tick < 20; tick++)
            world.Update(Delta);

        snake.Place(new Vector3(5, 5, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2));

        Assert.Equal(5, snake.PartPositions[0].X, 3);
        Assert.Equal(5, snake.PartPositions[0].Y, 3);
        // Obrócony o 90°: ogon ciągnie się wzdłuż −Y.
        Assert.Equal(5 - WorldObjectCatalog.SnakeSpacing, snake.PartPositions[1].Y, 3);
        world.Update(Delta);
        Assert.InRange(snake.Body.Position.X, 4.8f, 5.2f);
    }

    [Fact]
    public void Joints_FollowSpineCommands()
    {
        using var world = FloorWorld();
        var snake = WorldObjectCatalog.CreateSnake(Vector3.Zero, 0, WorldObjectCatalog.NeuralColor, null, WorldObjectCatalog.CreateCpg(4), 4);
        world.Add(snake);
        // Bez węzła kręgosłupa mózg nie nadpisuje komend — stawy zadaje test.
        snake.Brain!.Graph.Remove(snake.Brain.Graph.Modules.OfType<ActuatorModule>().Single());
        var spine = snake.Body.Actuators.OfType<SpineActuator>().Single();

        for (var tick = 0; tick < 60; tick++)
        {
            spine.Apply(snake, new Dictionary<string, float> { [SpineActuator.YawPort(0)] = 0.5f }, Delta);
            world.Update(Delta);
        }

        var expected = 0.5f * snake.Plan.Joints[0].MaxYaw;
        Assert.InRange(MathF.Abs(snake.JointYaw(0)), expected * 0.7f, expected * 1.3f);
        Assert.InRange(MathF.Abs(snake.JointYaw(1)), 0, 0.15f);
        var sense = snake.Body.Sensors.OfType<JointSensor>().Single().Read(snake, world);
        Assert.InRange(MathF.Abs(sense[SpineActuator.YawPort(0)]), 0.35f, 0.65f);
    }

    [Fact]
    public void HandCpg_CrawlsTowardsTheTarget()
    {
        using var world = FloorWorld();
        var target = WorldObjectCatalog.CreateTargetBall(new Vector3(6, 0, 0));
        world.Add(target);
        var snake = WorldObjectCatalog.CreateSnake(Vector3.Zero, 0, WorldObjectCatalog.ControllerColor, target.Id, WorldObjectCatalog.CreateCpg());
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
        var target = WorldObjectCatalog.CreateTargetBall(new Vector3(0, 5, 0));
        world.Add(target);
        var snake = WorldObjectCatalog.CreateSnake(Vector3.Zero, 0, WorldObjectCatalog.ControllerColor, target.Id, WorldObjectCatalog.CreateCpg());
        world.Add(snake);

        for (var tick = 0; tick < 12 * 30; tick++)
            world.Update(Delta);

        var end = Vector3.Distance(snake.PartPositions[0], target.Body.Position);
        Assert.True(end < 1.5f, $"end {end:0.00} m");
    }

    [Fact]
    public void Evolution_TeachesARandomCpgToCrawl()
    {
        var rig = SeekRigs.SnakeWith(6);
        var cpg = WorldObjectCatalog.CreateCpg(6);
        var options = rig.DefaultOptions with { Seed = 3, EpisodesPerGeneration = 3, ValidationEpisodes = 4 };
        var task = new SeekTargetTask(cpg.CaptureState(), rig, options, cpg.Name);
        cpg.Randomize(new Random(7));
        var start = task.Validate(cpg.GetParameters());
        var evolution = new Evolution(cpg.GetParameters(), new EvolutionOptions { Seed = 7 });

        for (var generation = 0; generation < 6; generation++)
            evolution.Step(task.Evaluate);

        var learned = task.Validate(evolution.Best);
        Assert.True(learned > start + 0.2f, $"start {start:0.000}, learned {learned:0.000}");
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

        snake.SetSegments(12);

        Assert.Equal(12, snake.Segments);
        Assert.Equal(12, snake.PartPositions.Count);
        Assert.Equal(2 * 11, snake.Body.Actuators.OfType<SpineActuator>().Single().InputPorts.Count);
        Assert.Equal(2 * 11, cpg.OutputPorts.Count);
        Assert.True(parameters.SequenceEqual(cpg.GetParameters()));
        var spine = snake.Brain.Graph.Modules.OfType<ActuatorModule>().Single();
        Assert.Equal(2 * 11, snake.Brain.Graph.Connections.Count(aLink => aLink.SourceId == cpg.Id && aLink.TargetId == spine.Id));
        snake.Brain.Graph.Validate();
        for (var tick = 0; tick < 10; tick++)
            world.Update(Delta);

        snake.SetSegments(3);
        Assert.Equal(2 * 2, snake.Brain.Graph.Connections.Count(aLink => aLink.SourceId == cpg.Id && aLink.TargetId == spine.Id));
        world.Update(Delta);
        Assert.Same(SeekRigs.SnakeWith(3), SeekRigs.For(snake));
    }

    [Fact]
    public void SnakeTraining_EvaluatesCpgInItsRig()
    {
        var snake = WorldObjectCatalog.CreateLearningSnake(Vector3.Zero, 0, null, 5);
        var cpg = snake.Brain!.Graph.Modules.OfType<CpgModule>().Single();
        var rig = SeekRigs.For(snake);
        var options = rig.DefaultOptions with { EpisodesPerGeneration = 1, EpisodeSeconds = 1, ValidationEpisodes = 1 };
        var task = new SeekTargetTask(cpg.CaptureState(), rig, options, cpg.Name);

        Assert.Equal(CpgModule.Parameters, task.ParameterCount);
        var score = task.Evaluate(cpg.GetParameters(), 0);
        Assert.True(float.IsFinite(score));
        Assert.Same(cpg, TrainingController.FindTrainable(snake));
    }

    [Fact]
    public void SnakeScene_HasFloorSlabsTargetAndTwoSnakes()
    {
        var scene = WorldObjectCatalog.CreateSnakeScene();
        using var world = scene.World;
        Assert.Single(world.Entities.OfType<Floor>());
        Assert.True(world.Entities.OfType<Slab>().Count() >= 3);
        var target = Assert.Single(world.Entities.OfType<TargetBall>());
        Assert.Equal(Terrain.HeightAt(world, new Vector2(target.Body.Position.X, target.Body.Position.Y), target), target.Body.Position.Z);
        Assert.Equal(2, scene.Creatures.Count);
        var cpg = scene.Creatures.OfType<SnakeCreature>().Single(aSnake => aSnake.Brain!.Graph.Modules.OfType<CpgModule>().Any());
        Assert.Contains(cpg.Brain!.Snapshots, aSnapshot => aSnapshot.Label == "ręczne parametry");
        var neural = scene.Creatures.OfType<SnakeCreature>().Single(aSnake => aSnake != cpg);
        Assert.Single(neural.Brain!.Graph.Modules.OfType<NeuralNetworkModule>());
        world.Update(Delta);
    }
}

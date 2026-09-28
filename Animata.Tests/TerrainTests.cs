using System.Numerics;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Sensors;
using Animata.Core.Training;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Tests;

/// <summary>Płyty terenu, wysokość terenu i przyciąganie do niego, wąż z własną siecią.</summary>
public class TerrainTests
{
    private const float Delta = 1f / 30f;

    [Fact]
    public void HeightAt_TakesTheHighestSurface_AndRespectsRotation()
    {
        using var world = new World();
        world.Add(Floor.At(10, 10));
        var slab = WorldObjectCatalog.CreateSlab(new Vector3(1, 0, 0), new Vector3(2, 0.4f, 0.1f), MathF.PI / 2);
        world.Add(slab);
        Assert.Equal(0, Terrain.HeightAt(world, new Vector2(-3, 0)));
        Assert.Equal(0.1f, Terrain.HeightAt(world, new Vector2(1, 0.9f)), 1e-5f);
        Assert.Equal(0, Terrain.HeightAt(world, new Vector2(1.9f, 0)));
        Assert.Equal(0, Terrain.HeightAt(world, new Vector2(1, 0), slab));
        Assert.Equal(-7, Terrain.HeightAt(world, new Vector2(50, 50)) - 7);
    }

    [Fact]
    public void Snap_PutsTargetOnSlab_ButNotSlabOnItself()
    {
        using var world = new World();
        world.Add(Floor.At(10, 10));
        var slab = WorldObjectCatalog.CreateSlab(Vector3.Zero, new Vector3(2, 2, 0.08f));
        world.Add(slab);
        var target = WorldObjectCatalog.CreateTargetBall(new Vector3(0.5f, 0.5f, 0));
        world.Add(target);
        Assert.True(Terrain.Snap(world, target));
        Assert.Equal(0.08f, target.Body.Position.Z, 1e-5f);
        Assert.False(Terrain.Snap(world, slab));
        var overlapping = WorldObjectCatalog.CreateSlab(new Vector3(0.5f, 0, 0), new Vector3(2, 2, 0.05f));
        world.Add(overlapping);
        for (var round = 0; round < 3; round++)
        {
            Terrain.Snap(world, slab);
            Terrain.Snap(world, overlapping);
        }
        Assert.Equal(0, slab.Body.Position.Z);
        Assert.Equal(0, overlapping.Body.Position.Z);
        target.Body.Position = new Vector3(3, 3, 0.08f);
        Assert.True(Terrain.Snap(world, target));
        Assert.Equal(0, target.Body.Position.Z);
    }

    [Fact]
    public void Snake_LiesOnASlab_InPhysics()
    {
        using var world = new World();
        world.Add(Floor.At(20, 20));
        var slab = WorldObjectCatalog.CreateSlab(new Vector3(-1, 0, 0), new Vector3(4, 1, 0.1f), 0);
        world.Add(slab);
        var snake = new SnakeCreature(6);   // bez mózgu: leży, nie pełza
        snake.Place(new Vector3(0.4f, 0, 0.1f), Quaternion.Identity);
        world.Add(snake);
        for (var tick = 0; tick < 60; tick++)
            world.Update(Delta);
        foreach (var position in snake.PartPositions)
            Assert.InRange(position.Z, 0.1f + WorldObjectCatalog.SnakeRadius - 0.02f, 0.1f + WorldObjectCatalog.SnakeRadius + 0.02f);

        // Przesunięta płyta odtwarza się w fizyce: wąż spada na podłogę.
        slab.Body.Position = new Vector3(8, 8, 0);
        for (var tick = 0; tick < 60; tick++)
            world.Update(Delta);
        Assert.InRange(snake.PartPositions[0].Z, WorldObjectCatalog.SnakeRadius - 0.02f, WorldObjectCatalog.SnakeRadius + 0.02f);
    }

    [Fact]
    public void NeuralSnake_ChangesLength_KeepingWeightsAndConvertingSnapshots()
    {
        using var world = new World();
        world.Add(Floor.At(20, 20));
        var snake = WorldObjectCatalog.CreateNeuralSnake(Vector3.Zero, 0, null, 5);
        world.Add(snake);
        var network = snake.Brain!.Graph.Modules.OfType<NeuralNetworkModule>().Single();
        var before = (NeuralNetworkState)network.CaptureState()!;
        var snapshot = snake.Brain.Capture("przed");

        snake.SetSegments(7);
        Assert.Equal(12, network.Outputs.Count);
        Assert.Equal(12, network.Network.Layers[^1]);
        Assert.Equal("Yaw5", network.Outputs[^2].Port);
        var rows = network.Network.Weights[^1];
        for (var row = 0; row < 8; row++)
            Assert.Equal(before.Weights[^1][row], rows[row]);
        Assert.Equal(before.Weights[^1][6], rows[8]);   // Yaw4 ← Yaw3 (ostatni stary staw)
        Assert.Equal(before.Weights[^1][7], rows[11]);  // Pitch5 ← Pitch3
        Assert.Equal(before.Weights[0], network.Network.Weights[0]);
        var converted = snake.Brain.Snapshots.Single(aSnapshot => aSnapshot.Id == snapshot.Id);
        Assert.Equal(12, ((NeuralNetworkState)converted.Modules.Single(aModule => aModule.ModuleId == network.Id).State).Outputs.Length);
        for (var tick = 0; tick < 10; tick++)
            world.Update(Delta);

        snake.SetSegments(3);
        Assert.Equal("Yaw0,Pitch0,Yaw1,Pitch1", string.Join(",", network.Outputs.Select(aOutput => aOutput.Port)));
        snake.Brain.Restore(snake.Brain.Snapshots.Single(aSnapshot => aSnapshot.Id == snapshot.Id));
        snake.Brain.Graph.Validate();
        Assert.Equal(4, network.Outputs.Count);
        world.Update(Delta);
    }

    [Fact]
    public void SnakeRig_PutsSlabsOnTheWay_NotUnderTheSnake()
    {
        var rig = SeekRigs.SnakeWith(8);
        var episodes = SeekTargetTask.CreateEpisodes(rig.DefaultOptions, 0)
            .Concat(SeekTargetTask.CreateEpisodes(rig.DefaultOptions, 1)).ToList();
        Assert.Contains(episodes, aEpisode => aEpisode.Slabs is { Length: > 0 });
        foreach (var episode in episodes)
            foreach (var slab in episode.Slabs ?? [])
            {
                Assert.InRange(slab.Height, 0.03f, 0.1f);
                var back = -new Vector2(MathF.Cos(episode.Yaw), MathF.Sin(episode.Yaw));
                for (var step = 0f; step <= 8 * WorldObjectCatalog.SnakeSpacing; step += 0.1f)
                    Assert.True(Vector2.Distance(slab.Position, back * step) > slab.Size.Length() / 2);
            }

        // Wąż z siecią przechodzi próby na terenie bez błędów (cel może stać na płycie).
        var module = WorldObjectCatalog.CreateSnakeNeuralModule(8);
        var results = SeekTargetTask.Run(module, episodes.Take(2).ToList(), rig.DefaultOptions with { EpisodeSeconds = 1 }, rig);
        Assert.All(results, aResult => Assert.True(float.IsFinite(aResult.Cost)));
    }
}

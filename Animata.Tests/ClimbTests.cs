using System.Numerics;
using Animata.Core.Brains.Modules;
using Animata.Core.Persistence;
using Animata.Core.Training;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;
using static Animata.Tests.TestWorlds;

namespace Animata.Tests;

/// <summary>Drzewa i wspinanie węża (owinięcie wokół pnia, toczenie zwoju), kolory stworów.</summary>
public class ClimbTests
{
    private static (World World, Cylinder Tree, Creature Snake) Wrapped(CpgModule? aBrain = null)
    {
        var world = new World();
        world.Add(WorldObjectCatalog.CreateFloor(20, 20));
        var tree = SeekRigs.CreateClimbCylinder(Vector3.Zero, 4);
        world.Add(tree);
        var snake = aBrain is null ? TestWorlds.BareSnake(8) : WorldObjectCatalog.CreateSnake(Vector3.Zero, 0, Vector3.One, null, aBrain);
        Snake.WrapAround(snake, tree, 0.7f);
        world.Add(snake);
        return (world, tree, snake);
    }

    [Fact]
    public void Wrap_PutsEverySegmentAgainstTheTrunk_HeadUp()
    {
        var (world, tree, snake) = Wrapped();
        using (world)
        {
            Assert.True(Snake.IsClimber(snake));
            var contact = tree.Radius + Snake.SegmentRadius;
            foreach (var part in snake.PartPositions)
                Assert.InRange(new Vector2(part.X, part.Y).Length(), contact - 0.06f, contact + 0.02f);
            Assert.True(snake.PartPositions[0].Z > snake.PartPositions[^1].Z);
            Assert.True(snake.PartPositions.Min(aPart => aPart.Z) > 0.1f);
        }
    }

    [Fact]
    public void WrappedSnake_WithoutMoving_HoldsOnToTheBark()
    {
        var (world, _, snake) = Wrapped();
        using (world)
        {
            var start = snake.PartPositions.Average(aPart => aPart.Z);
            for (var tick = 0; tick < 90; tick++)
                world.Update(Delta);
            Assert.True(snake.PartPositions.Average(aPart => aPart.Z) > start - 0.15f);
        }
    }

    [Fact]
    public void ClimbRig_RollingCpgReachesTheTop()
    {
        var rig = SeekRigs.ClimbWith(8);
        var episodes = SeekTargetTask.CreateValidationEpisodes(rig.DefaultOptions).Take(3).ToList();
        var results = SeekTargetTask.Run(WorldObjectCatalog.CreateClimbingCpg(), episodes, rig.DefaultOptions, rig);
        Assert.True(results.Count(aResult => aResult.Reached) >= 2, string.Join(", ", results.Select(aResult => aResult.FinalGap.ToString("0.00"))));
    }

    [Fact]
    public void Climber_ChoosesTheClimbRig_AndSurvivesSaving()
    {
        using var world = WorldObjectCatalog.CreateClimbScene().World;
        var snakes = world.Entities.OfDesign(Snake.Design).ToList();
        Assert.All(snakes, aSnake => Assert.Same(SeekRigs.ClimbWith(Snake.Segments(aSnake)), SeekRigs.For(aSnake)));
        for (var tick = 0; tick < 15; tick++)
            world.Update(Delta);

        var json = WorldFile.ToJson(WorldFile.Capture(world, "t"));
        using var restored = WorldFile.Restore(WorldFile.FromJson(json)).World;
        Assert.Equal(json, WorldFile.ToJson(WorldFile.Capture(restored, "t")));
        Assert.Equal(2, restored.Entities.OfType<Cylinder>().Count());
        foreach (var snake in snakes)
        {
            var twin = (Creature)restored.Find(snake.Id)!;
            Assert.True(Snake.IsClimber(twin));
            // Poza z zapisanych kątów stawów — bez ugięć więzów, więc z dokładnością do kilku cm.
            var errors = snake.PartPositions.Select((aPart, aIndex) => Vector3.Distance(aPart, twin.PartPositions[aIndex])).ToList();
            Assert.True(errors.Max() < 0.12f && errors.Average() < 0.06f, string.Join(" ", errors.Select(aError => aError.ToString("0.00"))));
            Assert.Equal(snake.Color, twin.Color);
        }
        restored.Update(Delta);
    }

}

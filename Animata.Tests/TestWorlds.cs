using System.Numerics;
using Animata.Core.Training;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Tests;

/// <summary>Wspólne dane testów.</summary>
internal static class TestWorlds
{
    /// <summary>Krok świata w testach — jak w Studiu i w nauce.</summary>
    public const float Delta = 1f / 30f;

    /// <summary>Świat z podłogą <paramref name="aSize"/> × <paramref name="aSize"/> m (górą na z = 0).</summary>
    public static World Floor(float aSize)
    {
        var world = new World();
        world.Add(WorldObjectCatalog.CreateFloor(aSize, aSize));
        return world;
    }

    /// <summary>Słupek tuż przed maską: sterownik cofa, a po 1 s próby wciąż jest w trakcie cofania.</summary>
    public static SeekEpisode BlockedStart() =>
        new(0, new Vector2(7, 0), [new ObstacleSpec(new Vector2(1.4f, 0), 0.5f)]);

    public static SeekEpisode OpenRoad(float aYaw = 0) => new(aYaw, new Vector2(0, 6), []);

    /// <summary>Szybkie opcje nauki do testów kontrolera (sekundy zamiast minut).</summary>
    public static SeekTargetOptions Quick(SeekRig aRig, int aSeed) => aRig.DefaultOptions with
    {
        Seed = aSeed,
        EpisodesPerGeneration = 2,
        EpisodeSeconds = 3,
        ValidationEpisodes = 2
    };

    /// <summary>Wąż bez zmysłów, napędów i mózgu — samo ciało z <paramref name="aSegments"/> segmentami (testy fizyki).</summary>
    public static Creature BareSnake(int aSegments = Snake.DefaultSegments)
    {
        var snake = new Creature(Snake.Design);
        snake.SetValue(Snake.SegmentsSetting, aSegments);
        snake.Reshape();
        return snake;
    }
}

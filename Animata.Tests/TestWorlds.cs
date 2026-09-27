using System.Numerics;
using Animata.Core.Training;

namespace Animata.Tests;

/// <summary>Wspólne dane testów.</summary>
internal static class TestWorlds
{
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
}

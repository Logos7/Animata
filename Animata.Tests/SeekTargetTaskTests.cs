using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Training;
using Animata.Core.WorldObjects;

namespace Animata.Tests;

public class SeekTargetTaskTests
{
    /// <summary>
    /// Próby są niezależne: druga próba po pierwszej wychodzi tak samo jak ta sama próba zagrana osobno.
    /// Pierwsza próba jest ucinana w trakcie cofania, więc bez Reset sterownik wniósłby do drugiej swoją pamięć.
    /// </summary>
    [Fact]
    public void Episodes_DoNotLeakControllerMemory()
    {
        var rig = SeekRigs.Car;
        var options = rig.DefaultOptions with { EpisodeSeconds = 1 };
        var cutShort = TestWorlds.BlockedStart();
        var next = TestWorlds.OpenRoad(aYaw: 2.5f);

        var controller = WorldObjectCatalog.CreateAvoidController();
        var together = SeekTargetTask.Run(controller, [cutShort, next], options, rig);
        var alone = SeekTargetTask.Run(WorldObjectCatalog.CreateAvoidController(), [next], options, rig);

        Assert.Equal(alone[0], together[1]);
    }

    [Fact]
    public void AvoidAndSeek_Reset_ClearsManoeuvre()
    {
        var rig = SeekRigs.Car;
        var options = rig.DefaultOptions with { EpisodeSeconds = 1 };
        var used = WorldObjectCatalog.CreateAvoidController();
        SeekTargetTask.Run(used, [TestWorlds.BlockedStart()], options, rig);
        used.Reset();

        var episodes = new[] { TestWorlds.OpenRoad(2.5f) };
        Assert.Equal(
            SeekTargetTask.Run(WorldObjectCatalog.CreateAvoidController(), episodes, options, rig)[0],
            SeekTargetTask.Run(used, episodes, options, rig)[0]);
    }

    [Fact]
    public void CarController_ReachesTargetOnRandomRoutes()
    {
        var rig = SeekRigs.Car;
        var episodes = SeekTargetTask.CreateEpisodes(rig.DefaultOptions with { Seed = 11, EpisodesPerGeneration = 32 }, 0);
        var results = SeekTargetTask.Run(WorldObjectCatalog.CreateAvoidController(), episodes, rig.DefaultOptions, rig);

        Assert.Equal(32, results.Count(aResult => aResult.Reached));
        Assert.InRange(results.Average(aResult => aResult.Cost), 0.1f, 0.35f);
    }

    [Fact]
    public void DiskController_ReachesTargetOnRandomRoutes()
    {
        var rig = SeekRigs.Disk;
        var episodes = SeekTargetTask.CreateEpisodes(rig.DefaultOptions with { EpisodesPerGeneration = 32 }, 0);
        var results = SeekTargetTask.Run(new ApproachTargetModule(), episodes, rig.DefaultOptions, rig);

        Assert.Equal(32, results.Count(aResult => aResult.Reached));
    }

    [Fact]
    public void Validation_DoesNotDependOnTrainingSeed()
    {
        var template = (NeuralNetworkState)WorldObjectCatalog.CreateCarNeuralModule().CaptureState();
        var first = new SeekTargetTask(template, SeekRigs.Car, SeekRigs.Car.DefaultOptions with { Seed = 1, ValidationEpisodes = 3 });
        var second = new SeekTargetTask(template, SeekRigs.Car, SeekRigs.Car.DefaultOptions with { Seed = 999, ValidationEpisodes = 3 });

        Assert.Equal(first.ValidationEpisodes.Select(aEpisode => aEpisode.TargetOffset),
            second.ValidationEpisodes.Select(aEpisode => aEpisode.TargetOffset));
        Assert.NotEqual(first.EpisodesFor(0)[0].TargetOffset, second.EpisodesFor(0)[0].TargetOffset);

        var parameters = new float[first.ParameterCount];
        for (var index = 0; index < parameters.Length; index++)
            parameters[index] = MathF.Sin(index);
        Assert.Equal(first.Validate(parameters), second.Validate(parameters));
    }

    [Fact]
    public void TaskIgnoresTemplateWeights()
    {
        var template = WorldObjectCatalog.CreateCarNeuralModule();
        var options = SeekRigs.Car.DefaultOptions with { EpisodesPerGeneration = 1, EpisodeSeconds = 1, ValidationEpisodes = 1 };
        var parameters = template.Network.GetParameters();
        var before = new SeekTargetTask((NeuralNetworkState)template.CaptureState(), SeekRigs.Car, options).Evaluate(parameters, 0);

        template.Network.Randomize(new Random(7));
        var after = new SeekTargetTask((NeuralNetworkState)template.CaptureState(), SeekRigs.Car, options).Evaluate(parameters, 0);
        Assert.Equal(before, after);
    }
}

using System.Diagnostics;
using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Sensors;
using Animata.Core.Training;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Tests;

/// <summary>
/// Nauka uczy tego, co jest w scenie: rig dostaje ustawienia zmysłów i napędów stwora, a zmiana modułu albo ustawień
/// w trakcie nauki restartuje ją na nowych warunkach. Kontakt z przeszkodą liczy fizyka. Stwór może mieć kilka napędów.
/// </summary>
public class TrainingSetupTests : IDisposable
{
    private readonly SnapshotHistory _history = new();
    private readonly List<TrainingController> _controllers = [];

    public void Dispose()
    {
        foreach (var controller in _controllers)
            controller.Dispose();
    }

    [Fact]
    public void Rig_GetsTheCreaturesSlotSettings_ButNotItsTarget()
    {
        var car = WorldObjectCatalog.CreateNeuralCar(default, 0, Guid.NewGuid());
        Assert.Same(SeekRigs.Car, SeekRigs.For(car)); // domyślne ustawienia — ten sam rig

        car.Body.Sensors.OfType<RaySensor>().Single().Range = 5;
        car.Body.Actuators.OfType<SteeringDriveActuator>().Single().MaxSpeed = 1.5f;
        var rig = SeekRigs.For(car);
        Assert.NotSame(SeekRigs.Car, rig);

        var target = Guid.NewGuid();
        var trial = rig.CreateCreature(target, WorldObjectCatalog.CreateCarNeuralModule());
        Assert.Equal(5, trial.Body.Sensors.OfType<RaySensor>().Single().Range);
        Assert.Equal(1.5f, trial.Body.Actuators.OfType<SteeringDriveActuator>().Single().MaxSpeed);
        Assert.Equal(target, trial.Body.Sensors.OfType<TargetSensor>().Single().TargetId);
    }

    [Fact]
    public void ChangingTheNetworkInputs_RestartsTraining_OnTheNewSetup()
    {
        var training = Controller();
        var car = WorldObjectCatalog.CreateNeuralCar(default, 0, null);
        var network = (NeuralNetworkModule)TrainingController.FindTrainable(car)!;
        Assert.True(training.Start(car, aSeed: 1));
        Assert.Null(PollFor(training));

        network.Inputs[0].Expression = "Found * 0.5";
        var message = PollFor(training);
        Assert.NotNull(message);
        Assert.Contains("od nowa", message);
        Assert.True(training.IsTraining(car.Brain!));
        Assert.Null(PollFor(training)); // nowe warunki = warunki nauki, drugi restart nie nastąpi
    }

    [Fact]
    public void ChangingADriveSetting_RestartsTraining()
    {
        var training = Controller();
        var car = WorldObjectCatalog.CreateNeuralCar(default, 0, null);
        training.Start(car, aSeed: 1);
        car.Body.Actuators.OfType<SteeringDriveActuator>().Single().MaxSpeed = 2;
        Assert.Contains("od nowa", PollFor(training));
        Assert.True(training.IsTraining(car.Brain!));
    }

    [Fact]
    public void RemovingTheTrainedModule_StopsTraining()
    {
        var training = Controller();
        var car = WorldObjectCatalog.CreateNeuralCar(default, 0, null);
        training.Start(car, aSeed: 1);
        car.Brain!.Graph.Remove(TrainingController.FindTrainable(car)!);
        Assert.NotNull(PollFor(training));
        Assert.False(training.IsTraining(car.Brain));
    }

    [Fact]
    public void ObstacleContact_ComesFromPhysics()
    {
        using var world = new World();
        world.Add(WorldObjectCatalog.CreateFloor(20, 20));
        var touched = WorldObjectCatalog.CreateCylinder(new Vector3(1.05f, 0, 0), 0.5f); // przód autka na 0.6 m
        var near = WorldObjectCatalog.CreateCylinder(new Vector3(0, 1.2f, 0), 0.5f);     // obrys autka (0.6) sięga, bok (0.3) nie
        world.Add(touched);
        world.Add(near);
        var car = WorldObjectCatalog.CreateNeuralCar(Vector3.Zero, 0, null);
        world.Add(car);
        world.Update(1f / 30); // pierwszy krok: nos autka wchodzi w cylinder (potem fizyka go odpycha)

        Assert.True(car.Touches(touched));
        Assert.False(car.Touches(near));
    }

    [Fact]
    public void CreatureWithTwoActuators_GetsItsBrain_AndTrains()
    {
        if (EntityTypes.Find("lampRoller") is null)
            EntityTypes.Register(EntityType.Creature("lampRoller", "Toczek z lampą", "wheel", aBrain => new LampRoller(aBrain)));
        var roller = (LampRoller)EntityTypes.Find("lampRoller")!.Create();

        // Sterownik celu wystawia tylko porty kół — lampa zostaje wolna.
        WorldObjectCatalog.BuildBrain(roller.Brain!, new ApproachTargetModule());
        Assert.Equal(2, roller.Brain!.Graph.Modules.OfType<ActuatorModule>().Count());

        // Sterownik pokrywający napęd tylko częściowo to błąd.
        roller.Brain.Clear();
        Assert.Throws<ArgumentException>(() => WorldObjectCatalog.BuildBrain(roller.Brain,
            new ConstantModule(DiskDriveActuator.StepPort, 1)));

        var rig = SeekRigs.For(roller);
        var result = SeekTargetTask.Run(new ApproachTargetModule(),
            SeekTargetTask.CreateEpisodes(rig.DefaultOptions with { EpisodesPerGeneration = 1 }, 0),
            rig.DefaultOptions with { EpisodeSeconds = 1 }, rig);
        Assert.True(float.IsFinite(result.Single().Cost));
    }

    private TrainingController Controller()
    {
        var controller = new TrainingController(_history, 1000, TestWorlds.Quick);
        _controllers.Add(controller);
        return controller;
    }

    /// <summary>Odpytuje naukę tyle razy, żeby sprawdziła warunki; zwraca ostatni komunikat albo null.</summary>
    private static string? PollFor(TrainingController aTraining)
    {
        string? message = null;
        for (var poll = 0; poll < 30; poll++)
        {
            aTraining.Poll(out var current);
            message = current ?? message;
        }
        return message;
    }
}

/// <summary>Toczek z dwoma napędami: koła i lampa (port Light) — mózg i nauka nie zakładają jednego napędu.</summary>
public sealed class LampRoller(Brain? aBrain = null) : ArticulatedCreature(RollerCreature.DefaultPlan(), aBrain)
{
    public override void Equip()
    {
        Body.Sensors.Add(new TargetSensor { Slot = "Eye" });
        Body.Actuators.Add(new DiskDriveActuator { Slot = "Wheels" });
        Body.Actuators.Add(new LampActuator { Slot = "Lamp" });
        base.Equip();
    }

    // Rejestr jest wspólny dla testów, więc toczek spełnia ten sam kontrakt co każdy stwór (EntityTypeTests).
    public override IReadOnlyList<BrainPreset> BrainPresets =>
        [new("Sterownik celu", "koła do celu, lampa wolna", () => new ApproachTargetModule { Name = "Approach" }, true)];
}

public sealed class LampActuator : Actuator
{
    public float Light { get; private set; }

    public override IReadOnlyList<string> InputPorts => ["Light"];

    public override void Apply(Entity aOwner, IReadOnlyDictionary<string, float> aCommands, float aDelta) =>
        Light = aCommands.GetValueOrDefault("Light");
}

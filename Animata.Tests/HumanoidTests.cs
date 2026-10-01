using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Bodies;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Persistence;
using Animata.Core.Sensors;
using Animata.Core.Training;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Tests;

/// <summary>Humanoid, błędnik, automat stanów (stanie ↔ chód) i nauka kilku modułów jednego mózgu.</summary>
public class HumanoidTests : IDisposable
{
    private const float Delta = 1f / 30f;
    private readonly List<TrainingController> _controllers = [];

    public void Dispose()
    {
        foreach (var controller in _controllers)
            controller.Dispose();
    }

    [Fact]
    public void Humanoid_StandsWithoutControl()
    {
        using var world = new World();
        world.Add(WorldObjectCatalog.CreateFloor(10, 10));
        var humanoid = new Creature(Humanoid.Design);
        world.Add(humanoid);
        for (var tick = 0; tick < 90; tick++)
            world.Update(Delta);
        Assert.InRange(humanoid.PartPositions[0].Z, 0.8f, 0.95f);
        Assert.Equal(0, Humanoid.Posture(humanoid));
        Assert.Equal(18, Humanoid.Ports.Length);
    }

    [Fact]
    public void Knee_BendsOnlyOneWay_AndHasNoSidewaysAxis()
    {
        var plan = Humanoid.DefaultPlan();
        var knee = plan.Joints[Humanoid.Knee(0)];
        Assert.Equal(0, knee.PitchMin);
        Assert.True(knee.MaxPitch > 2);
        Assert.DoesNotContain(SpineActuator.YawPort(Humanoid.Knee(0)), Humanoid.Ports);
        Assert.Contains(SpineActuator.PitchPort(Humanoid.Knee(0)), Humanoid.Ports);
        // Komenda „wstecz” to kąt zero, a nie przeprost.
        Assert.Equal(0, JointPlan.Angle(-1, knee.PitchMin, knee.MaxPitch));
    }

    [Fact]
    public void BalanceSensor_ReadsForwardLean_AsPositivePitch()
    {
        var forward = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f);   // przód (X) opada
        var (pitch, roll) = BalanceSensor.Angles(forward);
        Assert.Equal(0.3f, pitch, 3);
        Assert.Equal(0, roll, 3);
        var right = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.2f);      // lewy bok (Y) w górę
        Assert.Equal(0.2f, BalanceSensor.Angles(right).Roll, 3);
    }

    [Fact]
    public void StateMachine_Switches_Blends_AndWaitsTheDwellTime()
    {
        var machine = new StateMachineModule(["Stój", "Idź"], ["Found", "Gap"], ["X"],
            [new StateTransition("Stój", "Idź", "Found * Gap", true, 0.8f), new StateTransition("Idź", "Stój", "Gap", false, 0.4f)]);
        var context = new BrainContext(null!, null!, 0.1f);
        var inputs = new Dictionary<string, float> { ["Found"] = 1, ["Gap"] = 0.2f, ["Stój.X"] = -1, ["Idź.X"] = 1 };

        Assert.Equal(-1, machine.Evaluate(inputs, context)["X"]);   // cel blisko — stoi
        inputs["Gap"] = 3;
        var first = machine.Evaluate(inputs, context);
        Assert.Equal("Idź", machine.CurrentName);
        Assert.Equal(1, first[StateMachineModule.StatePort]);
        Assert.InRange(first["X"], -0.6f, -0.4f);                  // 0.1 s z 0.4 s przejścia: 25% chodu
        for (var tick = 0; tick < 4; tick++)
            machine.Evaluate(inputs, context);
        Assert.Equal(1, machine.Evaluate(inputs, context)["X"]);   // po przejściu — sam chód

        inputs["Gap"] = 0.2f;                                        // cel blisko, ale minęło dopiero 0.6 s
        machine.Evaluate(inputs, context);
        Assert.Equal("Idź", machine.CurrentName);
        for (var tick = 0; tick < 5; tick++)
            machine.Evaluate(inputs, context);
        Assert.Equal("Stój", machine.CurrentName);

        machine.Reset();
        Assert.Equal(0, machine.Current);
        Assert.Throws<ArgumentException>(() => machine.SetTransitions([new StateTransition("Stój", "Biegnij", "Gap", true, 1)]));
        Assert.Throws<ArgumentException>(() => machine.SetTransitions([new StateTransition("Stój", "Idź", "Distance", true, 1)]));
    }

    [Fact]
    public void StateBrain_HasTwoNetworks_AndSurvivesSaveAndLoad()
    {
        var humanoid = WorldObjectCatalog.Create(Humanoid.Design, Vector3.Zero, 0, null, HumanoidBrains.NetworkPreset);
        var graph = humanoid.Brain!.Graph;
        Assert.Equal(2, graph.Modules.OfType<NeuralNetworkModule>().Count());
        var machine = graph.Modules.OfType<StateMachineModule>().Single();
        Assert.Equal([HumanoidBrains.StandState, HumanoidBrains.WalkState], machine.States);
        graph.Validate();

        machine.BlendSeconds = 0.25f;
        var json = BrainFile.ToJson(BrainFile.Capture(humanoid.Brain, "humanoid"));
        var copy = WorldObjectCatalog.Create(Humanoid.Design, Vector3.Zero, 0, null, HumanoidBrains.NetworkPreset);
        BrainFile.Load(copy.Brain!, BrainFile.FromJson(json));
        Assert.Equal(json, BrainFile.ToJson(BrainFile.Capture(copy.Brain!, "humanoid")));
        Assert.Equal(0.25f, copy.Brain!.Graph.Modules.OfType<StateMachineModule>().Single().BlendSeconds);
    }

    [Theory]
    [InlineData(4f, HumanoidBrains.WalkState)]
    [InlineData(0.6f, HumanoidBrains.StandState)]
    public void StateBrain_WalksToAFarTarget_AndStandsNearOne(float aDistance, string aExpected)
    {
        using var world = new World();
        world.Add(WorldObjectCatalog.CreateFloor(20, 20));
        var target = WorldObjectCatalog.CreateSphere(new Vector3(aDistance, 0, 0));
        world.Add(target);
        Terrain.Snap(world, target);
        var humanoid = WorldObjectCatalog.Create(Humanoid.Design, Vector3.Zero, 0, target.Id, HumanoidBrains.HandPreset);
        world.Add(humanoid);
        for (var tick = 0; tick < 15; tick++)
            world.Update(Delta);
        Assert.Equal(aExpected, humanoid.Brain!.Graph.Modules.OfType<StateMachineModule>().Single().CurrentName);
    }

    [Fact]
    public void HandStateBrain_WalksToTheBall_Stops_AndFollowsItWhenMoved()
    {
        using var world = new World();
        world.Add(WorldObjectCatalog.CreateFloor(30, 30));
        var ball = WorldObjectCatalog.CreateSphere(new Vector3(4, 0, 0));
        world.Add(ball);
        Terrain.Snap(world, ball);
        var humanoid = WorldObjectCatalog.Create(Humanoid.Design, new Vector3(0, -1, 0), 0, ball.Id, HumanoidBrains.HandPreset);
        world.Add(humanoid);
        var machine = humanoid.Brain!.Graph.Modules.OfType<StateMachineModule>().Single();
        float Distance() => Vector2.Distance(new Vector2(humanoid.PartPositions[0].X, humanoid.PartPositions[0].Y),
            new Vector2(ball.Body.Position.X, ball.Body.Position.Y));
        void Run(float aSeconds)
        {
            for (var tick = 0; tick < aSeconds / Delta; tick++)
                world.Update(Delta);
        }

        Run(14);
        Assert.Equal(HumanoidBrains.StandState, machine.CurrentName);
        Assert.True(Distance() < 1.3f, $"odległość {Distance()}");
        var stopped = humanoid.PartPositions[0];
        Run(4);
        Assert.True(Vector3.Distance(stopped, humanoid.PartPositions[0]) < 0.05f, "stoi w miejscu");

        ball.Body.Position = new Vector3(6, 3, ball.Body.Position.Z);
        Run(1);
        Assert.Equal(HumanoidBrains.WalkState, machine.CurrentName);
        Run(13);
        Assert.Equal(HumanoidBrains.StandState, machine.CurrentName);
        Assert.True(Distance() < 1.3f, $"odległość {Distance()}");
        Assert.Equal(0, Humanoid.Posture(humanoid));
    }

    [Fact]
    public void HandBalance_StandsUnderPushes_MoreOftenThanARigidBody()
    {
        var rig = SeekRigs.HumanoidStand;
        var episodes = SeekTargetTask.CreateValidationEpisodes(rig.DefaultOptions);
        Assert.All(episodes, aEpisode => Assert.NotNull(aEpisode.Push));
        int Stood(BrainModule aModule) =>
            SeekTargetTask.Run(aModule, episodes, rig.DefaultOptions, rig).Count(aResult => aResult.PostureTime < 1);
        var passive = Stood(new BalanceModule { Name = HumanoidBrains.StandName });
        var hand = Stood(new BalanceModule { Name = HumanoidBrains.StandName }.WithParameters(HumanoidBrains.HandBalance));
        Assert.True(hand > passive, $"ręczne {hand}, sztywne {passive}");
    }

    [Fact]
    public void HandGait_ReachesTheTarget_WithoutFalling()
    {
        var rig = SeekRigs.HumanoidWalk;
        var results = SeekTargetTask.Run(new BipedGaitModule(), SeekTargetTask.CreateValidationEpisodes(rig.DefaultOptions),
            rig.DefaultOptions, rig);
        Assert.True(results.Count(aResult => aResult.Reached) >= 5, $"doszedł {results.Count(aResult => aResult.Reached)}/{results.Count}");
        Assert.True(results.Count(aResult => aResult.PostureTime < 1) >= 7, $"stał {results.Count(aResult => aResult.PostureTime < 1)}/{results.Count}");
    }

    [Fact]
    public void OtherRigs_GetNoPushes()
    {
        Assert.All(SeekTargetTask.CreateValidationEpisodes(SeekRigs.Spider.DefaultOptions), aEpisode => Assert.Null(aEpisode.Push));
    }

    [Fact]
    public void Training_TrainsEachNetwork_OnItsOwnRig()
    {
        var training = new TrainingController(new SnapshotHistory(), 1000, TestWorlds.Quick);
        _controllers.Add(training);
        var humanoid = WorldObjectCatalog.Create(Humanoid.Design, Vector3.Zero, 0, null, HumanoidBrains.NetworkPreset);
        Assert.True(training.Start(humanoid, aSeed: 1), training.LastStartError);
        var sessions = training.ProgressesOf(humanoid.Brain!);
        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, aSession => aSession.Module == HumanoidBrains.StandName && aSession.Rig.StartsWith(SeekRigs.HumanoidStand.Name));
        Assert.Contains(sessions, aSession => aSession.Module == HumanoidBrains.WalkName && aSession.Rig.StartsWith(SeekRigs.HumanoidWalk.Name));
        Assert.NotNull(training.Stop(humanoid.Brain!, aSnapshot: false) ?? string.Empty);
        Assert.False(training.IsTraining(humanoid.Brain!));
    }
}

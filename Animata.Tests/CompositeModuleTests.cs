using System.Numerics;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;
using Xunit;

namespace Animata.Tests;

public class CompositeModuleTests
{
    /// <summary>Moduł testowy: liczy wywołania Reset i przepuszcza wejście "In" na wyjście "Out".</summary>
    private sealed class ProbeModule : BrainModule
    {
        private readonly Dictionary<string, float> _outputs = new() { ["Out"] = 0 };
        public int Resets;
        public override IReadOnlyList<string> InputPorts => ["In"];
        public override IReadOnlyList<string> OutputPorts => ["Out"];

        public override IReadOnlyDictionary<string, float> Evaluate(IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
        {
            _outputs["Out"] = aInputs.GetValueOrDefault("In");
            return _outputs;
        }

        public override void Reset() => Resets++;
    }

    private static (World World, CylinderCreature Owner) Stage()
    {
        var world = new World();
        var owner = new CylinderCreature(new Brain());
        world.Add(owner);
        return (world, owner);
    }

    private static CompositeModule RouterComposite()
    {
        var composite = new CompositeModule(["Select", "A", "B"], ["V"]) { Name = "Switch" };
        var router = composite.Inner.Add(new RouterModule(2, "V"));
        composite.Inner.Connect(composite.Input, "Select", router, RouterModule.SelectPort);
        composite.Inner.Connect(composite.Input, "A", router, RouterModule.ChannelPort(0, "V"));
        composite.Inner.Connect(composite.Input, "B", router, RouterModule.ChannelPort(1, "V"));
        composite.Inner.Connect(router, "V", composite.Output, "V");
        return composite;
    }

    [Fact]
    public void Composite_PassesValuesThroughItsInnerGraph()
    {
        var (world, owner) = Stage();
        var graph = owner.Brain!.Graph;
        var select = graph.Add(new ConstantModule("Value", 1));
        var a = graph.Add(new ConstantModule("Value", 3));
        var b = graph.Add(new ConstantModule("Value", 7));
        var composite = graph.Add(RouterComposite());
        graph.Connect(select, "Value", composite, "Select");
        graph.Connect(a, "Value", composite, "A");
        graph.Connect(b, "Value", composite, "B");

        world.Update(0.1f);
        Assert.Equal(7f, graph.LastOutputs(composite)!["V"]);

        select.Value = 0;
        world.Update(0.1f);
        Assert.Equal(3f, graph.LastOutputs(composite)!["V"]);
    }

    [Fact]
    public void UnconnectedCompositeOutput_IsZeroNotMissing()
    {
        var (world, owner) = Stage();
        var composite = owner.Brain!.Graph.Add(new CompositeModule([], ["Free"]));
        world.Update(0.1f);
        Assert.Equal(0f, owner.Brain.Graph.LastOutputs(composite)!["Free"]);
    }

    [Fact]
    public void LastOutputs_IsNullAfterTheGraphChanges()
    {
        var (world, owner) = Stage();
        var graph = owner.Brain!.Graph;
        var constant = graph.Add(new ConstantModule("Value", 2));
        world.Update(0.1f);
        Assert.Equal(2f, graph.LastOutputs(constant)!["Value"]);

        graph.Add(new ConstantModule());
        Assert.Null(graph.LastOutputs(constant));
    }

    [Fact]
    public void GroupedController_DrivesTheCarExactlyLikeTheFlatOne()
    {
        (World, CarCreature) Build()
        {
            var world = new World();
            var target = WorldObjectCatalog.CreateTargetBall(new Vector3(7, 1, 0));
            world.Add(target);
            world.Add(WorldObjectCatalog.CreateObstacle(new Vector3(3.5f, 0.4f, 0), 0.7f));
            var car = WorldObjectCatalog.CreateControllerCar(Vector3.Zero, 0, target.Id);
            world.Add(car);
            return (world, car);
        }

        var (flatWorld, flatCar) = Build();
        var (groupedWorld, groupedCar) = Build();
        var graph = groupedCar.Brain!.Graph;
        var controller = graph.Modules.OfType<AvoidAndSeekModule>().Single();
        var composite = BrainGraphEditing.Group(graph, [controller], "Sterowanie");

        Assert.Contains(composite, graph.Modules);
        Assert.DoesNotContain(controller, graph.Modules);
        Assert.Equal(controller.InputPorts.OrderBy(aPort => aPort), composite.InputPorts.OrderBy(aPort => aPort));
        Assert.Equal(controller.OutputPorts, composite.OutputPorts);

        for (var tick = 0; tick < 240; tick++)
        {
            flatWorld.Update(1f / 30f);
            groupedWorld.Update(1f / 30f);
        }
        Assert.Equal(flatCar.Body.Position, groupedCar.Body.Position);
        Assert.Equal(flatCar.Body.Rotation, groupedCar.Body.Rotation);
    }

    [Fact]
    public void Reset_ReachesModulesInsideSubgraphs()
    {
        var (_, owner) = Stage();
        var outer = owner.Brain!.Graph.Add(new CompositeModule());
        var middle = outer.Inner.Add(new CompositeModule());
        var probe = middle.Inner.Add(new ProbeModule());

        owner.Brain.Reset();
        Assert.Equal(1, probe.Resets);
    }

    [Fact]
    public void ActuatorDrivenFromTwoLevels_FailsValidation()
    {
        var (_, owner) = Stage();
        var actuatorId = Guid.NewGuid();
        var graph = owner.Brain!.Graph;
        graph.Add(new ActuatorModule(actuatorId, ["Turn"]));
        var composite = graph.Add(new CompositeModule());
        composite.Inner.Add(new ActuatorModule(actuatorId, ["Turn"]));

        Assert.Throws<BrainException>(() => graph.Validate());
    }

    [Fact]
    public void CompositeContainingItself_FailsValidation()
    {
        var (_, owner) = Stage();
        var composite = owner.Brain!.Graph.Add(new CompositeModule());
        composite.Inner.Add(composite);

        Assert.Throws<BrainException>(() => owner.Brain.Graph.Validate());
    }

    [Fact]
    public void ErrorInsideSubgraph_PointsAtTheInnerModule()
    {
        var (world, owner) = Stage();
        var graph = owner.Brain!.Graph;
        var select = graph.Add(new ConstantModule("Value", float.NaN));
        var composite = graph.Add(RouterComposite());
        graph.Connect(select, "Value", composite, "Select");
        var router = composite.Children.OfType<RouterModule>().Single();

        var exception = Assert.Throws<BrainException>(() => world.Update(0.1f));
        Assert.Equal(router.Id, exception.ModuleId);
    }

    [Fact]
    public void Snapshot_CapturesAndRestoresModulesInsideSubgraphs()
    {
        var (_, owner) = Stage();
        var brain = owner.Brain!;
        var composite = brain.Graph.Add(new CompositeModule());
        var constant = composite.Inner.Add(new ConstantModule("Value", 1.5f));

        var snapshot = brain.Capture("przed");
        var entry = Assert.Single(snapshot.Modules);
        Assert.Equal(composite.Id, entry.ModuleId);
        Assert.True(entry.State.SameAs(ModuleState.FromJson(entry.State.ToJson())));

        constant.Value = -4;
        Assert.False(brain.Matches(snapshot));
        brain.Restore(snapshot);
        Assert.Equal(1.5f, constant.Value);
        Assert.True(brain.Matches(snapshot));
    }

    [Fact]
    public void SnapshotTakenBeforeGrouping_StillRestoresTheGroupedModule()
    {
        var car = WorldObjectCatalog.CreateNeuralCar(Vector3.Zero, 0, null);
        var brain = car.Brain!;
        var network = brain.Graph.Modules.OfType<NeuralNetworkModule>().Single();
        var snapshot = brain.Capture("przed grupowaniem", network);
        var weights = network.Network.GetParameters();

        BrainGraphEditing.Group(brain.Graph, [network]);
        network.Network.Randomize(new Random(5));
        Assert.False(brain.Matches(snapshot));

        Assert.Equal(1, brain.Restore(snapshot));
        Assert.Equal(weights, network.Network.GetParameters());
        // Moduł w podgrafie nadal da się wskazać do snapshotu, a powtórka tego samego stanu nic nie dodaje.
        Assert.Null(brain.CaptureIfChanged("po", [network]));
    }
}

using System.Numerics;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.WorldObjects;
using Xunit;

namespace Animata.Tests;

public class BrainGraphEditingTests
{
    private static HashSet<BrainConnection> Links(BrainGraph aGraph) => aGraph.Connections.ToHashSet();

    [Fact]
    public void TryConnect_RejectsCyclesAndTakenInputs()
    {
        var graph = new BrainGraph();
        var a = graph.Add(new RouterModule(1, "V") { Name = "A" });
        var b = graph.Add(new RouterModule(1, "V") { Name = "B" });

        Assert.Null(BrainGraphEditing.TryConnect(graph, a, "V", b, RouterModule.ChannelPort(0, "V")));
        Assert.NotNull(BrainGraphEditing.TryConnect(graph, b, "V", a, RouterModule.ChannelPort(0, "V")));
        Assert.NotNull(BrainGraphEditing.TryConnect(graph, a, "V", b, RouterModule.ChannelPort(0, "V")));
        Assert.NotNull(BrainGraphEditing.TryConnect(graph, a, "V", a, RouterModule.SelectPort));
        Assert.NotNull(BrainGraphEditing.TryConnect(graph, a, "Nope", b, RouterModule.SelectPort));
        Assert.Single(graph.Connections);
    }

    [Fact]
    public void Group_TurnsCrossingLinksIntoSharedPorts()
    {
        var car = WorldObjectCatalog.CreateNeuralCar(Vector3.Zero, 0, null);
        var graph = car.Brain!.Graph;
        var network = graph.Modules.OfType<NeuralNetworkModule>().Single();
        var wheels = graph.Modules.OfType<ActuatorModule>().Single();

        Assert.Throws<ArgumentException>(() => BrainGraphEditing.Group(graph, [network, wheels], "Napęd"));
        var composite = BrainGraphEditing.Group(graph, [network], "Sieć");

        // Każde wyjście sensora wchodzi raz, każde wyjście sieci wychodzi raz do kół (zostają na wierzchu, jak zmysły).
        Assert.Equal(network.InputPorts.Count, composite.InputPorts.Count);
        Assert.Equal(network.OutputPorts.Count, composite.OutputPorts.Count);
        Assert.Equal(network.InputPorts.Count + network.OutputPorts.Count, graph.Connections.Count);
        Assert.All(graph.Connections, aLink => Assert.True(aLink.TargetId == composite.Id || aLink.SourceId == composite.Id));
        Assert.Equal(4, graph.Modules.Count);
    }

    [Fact]
    public void Group_ThatWouldCloseACycle_ChangesNothing()
    {
        var graph = new BrainGraph();
        var a = graph.Add(new RouterModule(1, "V") { Name = "A" });
        var x = graph.Add(new RouterModule(1, "V") { Name = "X" });
        var b = graph.Add(new RouterModule(1, "V") { Name = "B" });
        graph.Connect(a, "V", x, RouterModule.SelectPort);
        graph.Connect(x, "V", b, RouterModule.SelectPort);
        graph.Positions[a.Id] = new Vector2(1, 2);
        var modules = graph.Modules.ToList();
        var links = Links(graph);

        Assert.Throws<InvalidOperationException>(() => BrainGraphEditing.Group(graph, [a, b]));

        Assert.Equal(modules, graph.Modules);
        Assert.Equal(links, Links(graph));
        Assert.Equal(new Vector2(1, 2), graph.Positions[a.Id]);
        graph.Validate();
    }

    [Fact]
    public void Ungroup_RestoresTheOriginalWiring()
    {
        var car = WorldObjectCatalog.CreateControllerCar(Vector3.Zero, 0, null);
        var graph = car.Brain!.Graph;
        BrainGraphEditing.AutoLayout(graph);
        var modules = graph.Modules.ToHashSet();
        var links = Links(graph);
        var controller = graph.Modules.OfType<AvoidAndSeekModule>().Single();

        var composite = BrainGraphEditing.Group(graph, [controller]);
        var moved = BrainGraphEditing.Ungroup(graph, composite);

        Assert.Single(moved);
        Assert.Equal(modules, graph.Modules.ToHashSet());
        Assert.Equal(links, Links(graph));
        Assert.All(graph.Modules, aModule => Assert.True(graph.Positions.ContainsKey(aModule.Id)));
    }

    [Fact]
    public void RenameAndRemovePort_FixParentConnections()
    {
        var graph = new BrainGraph();
        var source = graph.Add(new ConstantModule("Value", 1));
        var composite = graph.Add(new CompositeModule(["In"], []));
        var inside = composite.Inner.Add(new RouterModule(1, "V"));
        composite.Inner.Connect(composite.Input, "In", inside, RouterModule.SelectPort);
        graph.Connect(source, "Value", composite, "In");

        BrainGraphEditing.RenamePort(graph, composite, true, "In", "Wybór");
        graph.Validate();
        Assert.Equal("Wybór", graph.Connections.Single().TargetPort);
        Assert.Equal("Wybór", composite.Inner.Connections.Single().SourcePort);

        Assert.Equal("Port", BrainGraphEditing.AddPort(graph, composite, true));
        Assert.Equal("Port2", BrainGraphEditing.AddPort(graph, composite, true));

        Assert.True(BrainGraphEditing.RemovePort(graph, composite, true, "Wybór"));
        Assert.Empty(graph.Connections);
        Assert.Empty(composite.Inner.Connections);
        graph.Validate();
    }

}

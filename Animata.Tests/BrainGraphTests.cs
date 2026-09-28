using Animata.Core.Actuators;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Worlds;
using Animata.Core.WorldObjects;

namespace Animata.Tests;

public class BrainGraphTests
{
    [Fact]
    public void Cycle_IsRejected()
    {
        var graph = new BrainGraph();
        var a = graph.Add(new RouterModule(1, "X"));
        var b = graph.Add(new RouterModule(1, "X"));
        graph.Connect(a, "X", b, RouterModule.ChannelPort(0, "X"));
        graph.Connect(b, "X", a, RouterModule.ChannelPort(0, "X"));

        Assert.Contains("cycle", Assert.Throws<BrainException>(graph.Validate).Message);
    }

    [Fact]
    public void ActuatorDrivenTwice_IsRejected()
    {
        var drive = new DiskDriveActuator();
        var graph = new BrainGraph();
        graph.Add(new ActuatorModule(drive));
        graph.Add(new ActuatorModule(drive));

        Assert.Throws<BrainException>(graph.Validate);
    }

    [Fact]
    public void UnknownPortAndDoubleFeed_AreRejected()
    {
        var graph = new BrainGraph();
        var one = graph.Add(new ConstantModule("A", 1));
        var two = graph.Add(new ConstantModule("A", 2));
        var router = graph.Add(new RouterModule(1, "X"));
        graph.Connect(one, "A", router, "Nope");
        Assert.Throws<BrainException>(graph.Validate);

        graph.Connections.Clear();
        graph.Connect(one, "A", router, RouterModule.SelectPort);
        graph.Connect(two, "A", router, RouterModule.SelectPort);
        Assert.Throws<BrainException>(graph.Validate);
    }

    [Fact]
    public void UnconnectedInput_ReadsZero_AndRouterPicksChannel()
    {
        var creature = WorldObjectCatalog.CreateControllerSeeker(default, null);
        var graph = creature.Brain!.Graph;
        var select = graph.Add(new ConstantModule("Value", 1));
        var high = graph.Add(new ConstantModule("Value", 5));
        var router = graph.Add(new RouterModule(2, "X"));
        graph.Connect(select, "Value", router, RouterModule.SelectPort);
        graph.Connect(high, "Value", router, RouterModule.ChannelPort(1, "X"));

        creature.Brain.Think(creature, new World(), 0.1f);
        Assert.Equal(1, router.ActiveChannel);

        select.Value = 0; // kanał 0 nie jest podłączony → 0
        var outputs = router.Evaluate(new Dictionary<string, float> { [RouterModule.SelectPort] = 0 }, default);
        Assert.Equal(0f, outputs["X"]);
    }

}

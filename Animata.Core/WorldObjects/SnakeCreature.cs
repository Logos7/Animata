using Animata.Core.Actuators;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Sensors;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Wąż: <see cref="Segments"/> kapsuł połączonych przegubami kulowymi (skręt i pochylenie — pełne 3D), głowa to Seg0
/// z okiem. Kręgosłup (<see cref="SpineActuator"/>, slot „Spine”) zadaje stawy, czucie stawów (<see cref="JointSensor"/>,
/// slot „Joints”) je mierzy. Pełza dzięki łuskom (tarcie kierunkowe segmentów) i fali stawów, zwykle z <see cref="CpgModule"/>.
/// Liczbę segmentów zmienia się w miejscu (<see cref="SetSegments"/>) — ten sam stwór i mózg.
/// </summary>
public sealed class SnakeCreature : ArticulatedCreature
{
    public SnakeCreature(int aSegments, Brain? aBrain = null) : base(WorldObjectCatalog.SnakePlan(aSegments), aBrain)
    {
        Segments = aSegments;
    }

    public int Segments { get; private set; }

    /// <summary>
    /// Nowa liczba segmentów w miejscu: ciało przebudowane w pozie spoczynkowej przy głowie, porty kręgosłupa,
    /// czucia stawów i CPG idą za liczbą stawów, a połączenia „port do portu tej samej nazwy” (Yaw3 → Yaw3)
    /// obejmują nowe stawy. Parametry CPG zostają (nie zależą od długości węża).
    /// </summary>
    public void SetSegments(int aSegments)
    {
        if (!WorldObjectCatalog.IsValidSnakeLength(aSegments))
            throw new ArgumentOutOfRangeException(nameof(aSegments), aSegments,
                $"Wąż ma od {WorldObjectCatalog.MinSnakeSegments} do {WorldObjectCatalog.MaxSnakeSegments} segmentów.");
        if (aSegments == Segments)
            return;

        var joints = aSegments - 1;
        var graphs = Brain is null ? new List<BrainGraph>() : PortRewiring.GraphsOf(Brain.Graph).ToList();
        var pairs = graphs.Select(aGraph => (Graph: aGraph, Pairs: PortRewiring.SameNamePairs(aGraph, IsSpinePort))).ToList();

        Rebuild(WorldObjectCatalog.SnakePlan(aSegments));
        Segments = aSegments;
        foreach (var actuator in Body.Actuators.OfType<SpineActuator>())
            actuator.SetJointCount(joints);
        foreach (var sensor in Body.Sensors.OfType<JointSensor>())
            sensor.SetJointCount(joints);

        foreach (var module in graphs.SelectMany(aGraph => aGraph.Modules))
            switch (module)
            {
                case ActuatorModule output when Body.FindActuator(output.Slot) is SpineActuator spine:
                    output.SetPorts(spine.InputPorts);
                    break;
                case SensorModule input when Body.FindSensor(input.Slot) is JointSensor sense:
                    input.SetPorts(sense.OutputPorts);
                    break;
                case CpgModule cpg:
                    cpg.SetJointCount(joints);
                    break;
            }

        foreach (var (graph, links) in pairs)
            PortRewiring.Rewire(graph, links);
        if (Brain is null)
            return;
        Brain.Graph.InvalidateDeep();
        Brain.Graph.Validate();
        Brain.Reset();
    }

    /// <summary>Port stawu: Yaw{i} albo Pitch{i}.</summary>
    public static bool IsSpinePort(string aPort) =>
        (aPort.StartsWith("Yaw", StringComparison.Ordinal) && aPort.Length > 3 && aPort[3..].All(char.IsAsciiDigit)) ||
        (aPort.StartsWith("Pitch", StringComparison.Ordinal) && aPort.Length > 5 && aPort[5..].All(char.IsAsciiDigit));
}

/// <summary>Wspólne kroki przepinania mózgu po zmianie portów ciała (wąsy, segmenty).</summary>
public static class PortRewiring
{
    /// <summary>Graf i wszystkie podgrafy (rekurencyjnie).</summary>
    public static IEnumerable<BrainGraph> GraphsOf(BrainGraph aGraph)
    {
        yield return aGraph;
        foreach (var composite in aGraph.Modules.OfType<CompositeModule>())
            foreach (var inner in GraphsOf(composite.Inner))
                yield return inner;
    }

    /// <summary>Pary modułów połączone „port do portu tej samej nazwy” na portach spełniających warunek.</summary>
    public static List<(BrainModule Source, BrainModule Target)> SameNamePairs(BrainGraph aGraph, Func<string, bool> aPort)
    {
        var pairs = new List<(BrainModule Source, BrainModule Target)>();
        foreach (var link in aGraph.Connections)
        {
            if (link.SourcePort != link.TargetPort || !aPort(link.SourcePort))
                continue;
            if (aGraph.Find(link.SourceId) is { } source && aGraph.Find(link.TargetId) is { } target && !pairs.Contains((source, target)))
                pairs.Add((source, target));
        }
        return pairs;
    }

    /// <summary>
    /// Usuwa połączenia z/do portów, których już nie ma, i łączy pary z <see cref="SameNamePairs"/> na wszystkich
    /// wspólnych nazwach portów, które mają wolne wejście.
    /// </summary>
    public static void Rewire(BrainGraph aGraph, List<(BrainModule Source, BrainModule Target)> aPairs)
    {
        aGraph.Connections.RemoveAll(aLink =>
            aGraph.Find(aLink.SourceId) is { } source && aGraph.Find(aLink.TargetId) is { } target &&
            (!source.OutputPorts.Contains(aLink.SourcePort) || !target.InputPorts.Contains(aLink.TargetPort)));

        foreach (var (source, target) in aPairs)
            foreach (var port in source.OutputPorts)
                if (target.InputPorts.Contains(port) &&
                    !aGraph.Connections.Any(aLink => aLink.TargetId == target.Id && aLink.TargetPort == port))
                    aGraph.Connect(source, port, target, port);
        aGraph.Invalidate();
    }
}

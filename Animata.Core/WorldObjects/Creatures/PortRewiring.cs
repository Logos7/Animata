using Animata.Core.Brains;
using Animata.Core.Brains.Modules;

namespace Animata.Core.WorldObjects;

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

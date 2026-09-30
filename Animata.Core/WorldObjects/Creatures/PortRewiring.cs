using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Zmiana portów ciała w miejscu (liczba wąsów, segmentów, …) — jedna procedura dla każdej takiej zmiany, żeby ten sam
/// stwór i ten sam mózg przeżyły ją razem ze snapshotami (<see cref="Change"/>).
/// </summary>
public static class PortRewiring
{
    /// <summary>
    /// Zmienia porty ciała stwora i dopasowuje do nich mózg:
    /// <list type="number">
    /// <item>najpierw wszystko, co może rzucić: <paramref name="aRemap"/> na stanie każdego modułu mózgu (także w podgrafach)
    /// — wyjątek przerywa zmianę, zanim cokolwiek się zmieni; snapshoty przeliczane tą samą funkcją (snapshot, którego
    /// nie da się przeliczyć — <see cref="InvalidOperationException"/> — jest usuwany);</item>
    /// <item><paramref name="aChangeBody"/> — zmiana ciała (i modułów, których porty zależą wprost od ciała, np. CPG);</item>
    /// <item>moduły dostają przeliczone stany, połączenia z/do znikniętych portów odpadają, a pary połączone „port do
    /// portu tej samej nazwy” (<paramref name="aFeeds"/>: źródło, port) dostają połączenia także na nowych portach;</item>
    /// <item>snapshoty podmienione, graf skompilowany, stan chwilowy wyczyszczony.</item>
    /// </list>
    /// <paramref name="aRemap"/> zwraca ten sam obiekt, gdy stanu nie trzeba zmieniać.
    /// </summary>
    public static void Change(ActiveEntity aCreature, Func<ModuleState, string, ModuleState> aRemap,
        Func<BrainModule, string, bool> aFeeds, Action<IReadOnlyList<BrainGraph>> aChangeBody)
    {
        var brain = aCreature.Brain;
        var graphs = brain is null ? [] : GraphsOf(brain.Graph).ToList();

        // 1. Wszystko, co może rzucić, zanim cokolwiek się zmieni.
        var states = new List<(BrainModule Module, ModuleState State)>();
        foreach (var module in graphs.SelectMany(aGraph => aGraph.Modules))
            if (module is not CompositeModule && module.CaptureState() is { } state &&
                aRemap(state, module.Name) is var remapped && !ReferenceEquals(remapped, state))
                states.Add((module, remapped));
        var snapshots = brain?.Snapshots.Select(aSnapshot => (Old: aSnapshot, New: Convert(aSnapshot, aRemap))).ToList() ?? [];
        var pairs = graphs.Select(aGraph => (Graph: aGraph, Pairs: SameNamePairs(aGraph, aFeeds))).ToList();

        // 2. Ciało.
        aChangeBody(graphs);

        // 3. Moduły, połączenia, snapshoty, kompilacja.
        foreach (var (module, state) in states)
            module.RestoreState(state);
        foreach (var (graph, links) in pairs)
            Rewire(graph, links);
        if (brain is null)
            return;
        foreach (var (old, converted) in snapshots)
            if (converted is null)
                brain.RemoveSnapshot(old);
            else if (!ReferenceEquals(converted, old))
                brain.ReplaceSnapshot(old, converted);
        brain.Graph.InvalidateDeep();
        brain.Graph.Validate();
        brain.Reset();
    }

    /// <summary>Snapshot przeliczony; ten sam obiekt, gdy nic się nie zmienia; null, gdy się nie da.</summary>
    private static BrainSnapshot? Convert(BrainSnapshot aSnapshot, Func<ModuleState, string, ModuleState> aRemap)
    {
        try
        {
            var modules = aSnapshot.Modules.Select(aModule => Convert(aModule, aRemap)).ToArray();
            return modules.SequenceEqual(aSnapshot.Modules) ? aSnapshot : aSnapshot with { Modules = modules };
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static ModuleSnapshot Convert(ModuleSnapshot aModule, Func<ModuleState, string, ModuleState> aRemap)
    {
        if (aModule.State is CompositeState composite)
        {
            var inner = composite.Modules.Select(aInner => Convert(aInner, aRemap)).ToArray();
            return inner.SequenceEqual(composite.Modules) ? aModule : aModule with { State = new CompositeState(inner) };
        }
        var remapped = aRemap(aModule.State, aModule.ModuleName);
        return ReferenceEquals(remapped, aModule.State) ? aModule : aModule with { State = remapped };
    }

    /// <summary>Graf i wszystkie podgrafy (rekurencyjnie).</summary>
    public static IEnumerable<BrainGraph> GraphsOf(BrainGraph aGraph)
    {
        yield return aGraph;
        foreach (var composite in aGraph.Modules.OfType<CompositeModule>())
            foreach (var inner in GraphsOf(composite.Inner))
                yield return inner;
    }

    /// <summary>Pary modułów połączone „port do portu tej samej nazwy” na portach spełniających warunek (źródło, port).</summary>
    private static List<(BrainModule Source, BrainModule Target)> SameNamePairs(BrainGraph aGraph, Func<BrainModule, string, bool> aFeeds)
    {
        var pairs = new List<(BrainModule Source, BrainModule Target)>();
        foreach (var link in aGraph.Connections)
        {
            if (link.SourcePort != link.TargetPort)
                continue;
            if (aGraph.Find(link.SourceId) is { } source && aFeeds(source, link.SourcePort) &&
                aGraph.Find(link.TargetId) is { } target && !pairs.Contains((source, target)))
                pairs.Add((source, target));
        }
        return pairs;
    }

    /// <summary>
    /// Usuwa połączenia z/do portów, których już nie ma, i łączy pary z <see cref="SameNamePairs"/> na wszystkich
    /// wspólnych nazwach portów, które mają wolne wejście.
    /// </summary>
    private static void Rewire(BrainGraph aGraph, List<(BrainModule Source, BrainModule Target)> aPairs)
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

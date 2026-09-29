using System.Numerics;
using Animata.Core.Brains.Modules;

namespace Animata.Core.Brains;

/// <summary>
/// Operacje edytora na grafie mózgu: bezpieczne łączenie, grupowanie w podgraf i rozgrupowanie, porty podgrafów,
/// automatyczny układ. Każda operacja zostawia graf poprawny albo nic nie zmienia i zgłasza błąd.
/// Po zmianie wołaj je na grafie, w którym moduł bezpośrednio siedzi (<see cref="BrainGraph.GraphOf"/>).
/// </summary>
public static class BrainGraphEditing
{
    /// <summary>Szacowany rozmiar węzła w edytorze (szerokość, wysokość) — do układania.</summary>
    public static Vector2 DefaultNodeSize(BrainModule aModule) =>
        new(170, 42 + 22 * Math.Max(1, Math.Max(aModule.InputPorts.Count, aModule.OutputPorts.Count)));

    /// <summary>Czy z <paramref name="aFrom"/> da się dojść połączeniami do <paramref name="aTo"/>.</summary>
    public static bool Reaches(BrainGraph aGraph, BrainModule aFrom, BrainModule aTo)
    {
        var seen = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(aFrom.Id);
        while (stack.TryPop(out var id))
        {
            if (id == aTo.Id)
                return true;
            if (!seen.Add(id))
                continue;
            foreach (var link in aGraph.Connections)
                if (link.SourceId == id)
                    stack.Push(link.TargetId);
        }
        return false;
    }

    /// <summary>Powód, dla którego połączenia nie da się zrobić, albo null, gdy można.</summary>
    public static string? CanConnect(BrainGraph aGraph, BrainModule aSource, string aSourcePort, BrainModule aTarget, string aTargetPort)
    {
        if (!aGraph.Modules.Contains(aSource) || !aGraph.Modules.Contains(aTarget))
            return "Oba moduły muszą być w tym samym grafie.";
        if (aSource == aTarget)
            return "Moduł nie może sterować sam sobą.";
        if (!aSource.OutputPorts.Contains(aSourcePort))
            return $"{aSource} nie ma wyjścia {aSourcePort}.";
        if (!aTarget.InputPorts.Contains(aTargetPort))
            return $"{aTarget} nie ma wejścia {aTargetPort}.";
        if (aGraph.Connections.Any(aLink => aLink.TargetId == aTarget.Id && aLink.TargetPort == aTargetPort))
            return $"Wejście {aTargetPort} jest już podłączone.";
        if (Reaches(aGraph, aTarget, aSource))
            return "To połączenie zamknęłoby cykl.";
        return null;
    }

    /// <summary>Łączy porty, jeśli się da i graf po zmianie się kompiluje; inaczej nic nie zmienia i zwraca powód.</summary>
    public static string? TryConnect(BrainGraph aGraph, BrainModule aSource, string aSourcePort, BrainModule aTarget, string aTargetPort)
    {
        if (CanConnect(aGraph, aSource, aSourcePort, aTarget, aTargetPort) is { } reason)
            return reason;
        var connection = new BrainConnection(aSource.Id, aSourcePort, aTarget.Id, aTargetPort);
        aGraph.Connections.Add(connection);
        try
        {
            aGraph.Validate();
            return null;
        }
        catch (BrainException exception)
        {
            aGraph.Connections.Remove(connection);
            aGraph.Invalidate();
            return exception.Message;
        }
    }

    /// <summary>
    /// Przenosi moduły do nowego podgrafu. Połączenia z zewnątrz do grupy stają się wejściami podgrafu (jedno wejście
    /// na każde użyte wyjście źródła), połączenia z grupy na zewnątrz — jego wyjściami. Id modułów się nie zmieniają,
    /// więc stare snapshoty dalej je przywracają. Rzuca InvalidOperationException (i nic nie zmienia), gdy grupa
    /// zamknęłaby cykl przez moduł spoza niej.
    /// </summary>
    public static CompositeModule Group(BrainGraph aGraph, IEnumerable<BrainModule> aModules, string aName = "Podgraf")
    {
        var group = aModules.Distinct().ToList();
        if (group.Count == 0)
            throw new ArgumentException("Nothing to group.", nameof(aModules));
        if (group.Any(aModule => !aGraph.Modules.Contains(aModule)))
            throw new ArgumentException("All grouped modules must belong to this graph.", nameof(aModules));
        if (group.Any(aModule => aModule is SubgraphInputModule or SubgraphOutputModule))
            throw new ArgumentException("Subgraph boundaries cannot be grouped.", nameof(aModules));
        if (group.Any(aModule => aModule is SensorModule or ActuatorModule))
            throw new ArgumentException("Zmysły i napędy należą do ciała i zostają na wierzchu mózgu — zgrupuj tylko logikę.", nameof(aModules));

        var ids = group.Select(aModule => aModule.Id).ToHashSet();
        var savedModules = aGraph.Modules.ToList();
        var savedConnections = aGraph.Connections.ToList();
        var savedPositions = aGraph.Positions.ToDictionary();

        var composite = new CompositeModule { Name = aName };
        var inner = composite.Inner;
        var inputs = new Dictionary<(Guid, string), string>();
        var outputs = new Dictionary<(Guid, string), string>();
        var parentLinks = new List<BrainConnection>();

        foreach (var link in savedConnections)
        {
            var fromInside = ids.Contains(link.SourceId);
            var toInside = ids.Contains(link.TargetId);
            if (fromInside && toInside)
                inner.Connections.Add(link);
            else if (toInside)
            {
                if (!inputs.TryGetValue((link.SourceId, link.SourcePort), out var port))
                {
                    port = Unique(composite.InputPorts, link.SourcePort);
                    composite.AddInputPort(port);
                    inputs.Add((link.SourceId, link.SourcePort), port);
                    parentLinks.Add(new BrainConnection(link.SourceId, link.SourcePort, composite.Id, port));
                }
                inner.Connections.Add(new BrainConnection(composite.Input.Id, port, link.TargetId, link.TargetPort));
            }
            else if (fromInside)
            {
                if (!outputs.TryGetValue((link.SourceId, link.SourcePort), out var port))
                {
                    port = Unique(composite.OutputPorts, link.SourcePort);
                    composite.AddOutputPort(port);
                    outputs.Add((link.SourceId, link.SourcePort), port);
                    inner.Connections.Add(new BrainConnection(link.SourceId, link.SourcePort, composite.Output.Id, port));
                }
                parentLinks.Add(new BrainConnection(composite.Id, port, link.TargetId, link.TargetPort));
            }
            else
                parentLinks.Add(link);
        }

        // Układ: wnętrze zachowuje położenia, granice po bokach; kompozyt w środku ciężkości grupy.
        var placed = group.Where(aModule => aGraph.Positions.ContainsKey(aModule.Id)).ToList();
        var center = placed.Count > 0
            ? placed.Aggregate(Vector2.Zero, (aSum, aModule) => aSum + aGraph.Positions[aModule.Id]) / placed.Count
            : Vector2.Zero;
        var index = group.Select(aModule => aGraph.Modules.IndexOf(aModule)).Min();
        foreach (var module in group)
        {
            aGraph.Modules.Remove(module);
            inner.Modules.Add(module);
            if (aGraph.Positions.Remove(module.Id, out var position))
                inner.Positions[module.Id] = position;
        }
        if (placed.Count > 0)
        {
            var minX = placed.Min(aModule => inner.Positions[aModule.Id].X);
            var maxX = placed.Max(aModule => inner.Positions[aModule.Id].X + DefaultNodeSize(aModule).X);
            var minY = placed.Min(aModule => inner.Positions[aModule.Id].Y);
            inner.Positions[composite.Input.Id] = new Vector2(minX - 260, minY);
            inner.Positions[composite.Output.Id] = new Vector2(maxX + 90, minY);
        }
        else
            AutoLayout(inner);

        aGraph.Modules.Insert(Math.Min(index, aGraph.Modules.Count), composite);
        aGraph.Positions[composite.Id] = center;
        aGraph.Connections.Clear();
        aGraph.Connections.AddRange(parentLinks);
        aGraph.InvalidateDeep();

        try
        {
            aGraph.Validate();
        }
        catch (BrainException exception)
        {
            aGraph.Modules.Clear();
            aGraph.Modules.AddRange(savedModules);
            aGraph.Connections.Clear();
            aGraph.Connections.AddRange(savedConnections);
            aGraph.Positions.Clear();
            foreach (var (id, position) in savedPositions)
                aGraph.Positions[id] = position;
            aGraph.InvalidateDeep();
            throw new InvalidOperationException($"Tych modułów nie da się zgrupować: {exception.Message}", exception);
        }
        return composite;
    }

    /// <summary>
    /// Wyciąga moduły podgrafu do grafu rodzica i przepina połączenia przez granice. Zwraca przeniesione moduły.
    /// </summary>
    public static IReadOnlyList<BrainModule> Ungroup(BrainGraph aGraph, CompositeModule aComposite)
    {
        if (!aGraph.Modules.Contains(aComposite))
            throw new ArgumentException("The composite is not part of this graph.", nameof(aComposite));

        var inner = aComposite.Inner;
        var input = aComposite.Input;
        var output = aComposite.Output;
        var children = aComposite.Children.ToList();
        var feeds = aGraph.Connections.Where(aLink => aLink.TargetId == aComposite.Id).ToList();
        var uses = aGraph.Connections.Where(aLink => aLink.SourceId == aComposite.Id).ToList();

        var links = aGraph.Connections.Where(aLink => aLink.SourceId != aComposite.Id && aLink.TargetId != aComposite.Id).ToList();
        foreach (var link in inner.Connections)
        {
            var sources = link.SourceId == input.Id
                ? feeds.Where(aFeed => aFeed.TargetPort == link.SourcePort).Select(aFeed => (aFeed.SourceId, aFeed.SourcePort)).ToList()
                : [(link.SourceId, link.SourcePort)];
            var targets = link.TargetId == output.Id
                ? uses.Where(aUse => aUse.SourcePort == link.TargetPort).Select(aUse => (aUse.TargetId, aUse.TargetPort)).ToList()
                : [(link.TargetId, link.TargetPort)];
            foreach (var (sourceId, sourcePort) in sources)
                foreach (var (targetId, targetPort) in targets)
                    links.Add(new BrainConnection(sourceId, sourcePort, targetId, targetPort));
        }

        var anchor = aGraph.Positions.GetValueOrDefault(aComposite.Id);
        var placed = children.Where(aModule => inner.Positions.ContainsKey(aModule.Id)).ToList();
        var center = placed.Count > 0
            ? placed.Aggregate(Vector2.Zero, (aSum, aModule) => aSum + inner.Positions[aModule.Id]) / placed.Count
            : Vector2.Zero;

        var index = aGraph.Modules.IndexOf(aComposite);
        aGraph.Modules.RemoveAt(index);
        aGraph.Positions.Remove(aComposite.Id);
        aGraph.Modules.InsertRange(index, children);
        foreach (var module in children)
        {
            inner.Modules.Remove(module);
            if (inner.Positions.Remove(module.Id, out var position))
                aGraph.Positions[module.Id] = anchor + position - center;
        }
        inner.Connections.Clear();
        aGraph.Connections.Clear();
        aGraph.Connections.AddRange(links);
        aGraph.InvalidateDeep();
        aGraph.Validate();
        return children;
    }

    /// <summary>Dodaje port podgrafu (wejście albo wyjście) o nazwie opartej na <paramref name="aName"/>. Zwraca nadaną nazwę.</summary>
    public static string AddPort(BrainGraph aParent, CompositeModule aComposite, bool aInput, string aName = "Port")
    {
        var port = Unique(aInput ? aComposite.InputPorts : aComposite.OutputPorts, aName);
        if (aInput)
            aComposite.AddInputPort(port);
        else
            aComposite.AddOutputPort(port);
        aParent.Invalidate();
        return port;
    }

    /// <summary>Usuwa port podgrafu razem z połączeniami, które przez niego szły (w rodzicu i we wnętrzu).</summary>
    public static bool RemovePort(BrainGraph aParent, CompositeModule aComposite, bool aInput, string aPort)
    {
        var removed = aInput ? aComposite.RemoveInputPort(aPort) : aComposite.RemoveOutputPort(aPort);
        if (removed)
        {
            aParent.Connections.RemoveAll(aLink => aInput
                ? aLink.TargetId == aComposite.Id && aLink.TargetPort == aPort
                : aLink.SourceId == aComposite.Id && aLink.SourcePort == aPort);
            aParent.Invalidate();
        }
        return removed;
    }

    /// <summary>Zmienia nazwę portu podgrafu razem z połączeniami rodzica i wnętrza.</summary>
    public static void RenamePort(BrainGraph aParent, CompositeModule aComposite, bool aInput, string aOld, string aNew)
    {
        if (aInput)
            aComposite.RenameInputPort(aOld, aNew);
        else
            aComposite.RenameOutputPort(aOld, aNew);
        for (var i = 0; i < aParent.Connections.Count; i++)
        {
            var link = aParent.Connections[i];
            if (aInput && link.TargetId == aComposite.Id && link.TargetPort == aOld)
                aParent.Connections[i] = link with { TargetPort = aNew };
            else if (!aInput && link.SourceId == aComposite.Id && link.SourcePort == aOld)
                aParent.Connections[i] = link with { SourcePort = aNew };
        }
        aParent.Invalidate();
    }

    /// <summary>
    /// Układ warstwowy: kolumna = najdłuższa droga od źródeł, w kolumnie kolejność jak w liście modułów.
    /// Granice podgrafu zawsze skrajnie po lewej i prawej.
    /// </summary>
    public static void AutoLayout(BrainGraph aGraph, Func<BrainModule, Vector2>? aSize = null, float aColumnGap = 80, float aRowGap = 36)
    {
        var size = aSize ?? DefaultNodeSize;
        var depth = aGraph.Modules.ToDictionary(aModule => aModule.Id, _ => 0);
        for (var pass = 0; pass < aGraph.Modules.Count; pass++)
        {
            var changed = false;
            foreach (var link in aGraph.Connections)
                if (depth.TryGetValue(link.SourceId, out var source) && depth.TryGetValue(link.TargetId, out var target) &&
                    target < source + 1)
                {
                    depth[link.TargetId] = source + 1;
                    changed = true;
                }
            if (!changed)
                break;
        }

        var last = depth.Values.DefaultIfEmpty(0).Max();
        foreach (var module in aGraph.Modules)
        {
            if (module is SubgraphInputModule)
                depth[module.Id] = 0;
            else if (module is SubgraphOutputModule)
                depth[module.Id] = Math.Max(last, 1);
        }

        var columns = aGraph.Modules.GroupBy(aModule => depth[aModule.Id]).OrderBy(aColumn => aColumn.Key).ToList();
        var x = 0f;
        foreach (var column in columns)
        {
            var y = 0f;
            var width = 0f;
            foreach (var module in column)
            {
                aGraph.Positions[module.Id] = new Vector2(x, y);
                var extent = size(module);
                y += extent.Y + aRowGap;
                width = MathF.Max(width, extent.X);
            }
            x += width + aColumnGap;
        }
    }

    /// <summary>Nazwa unikalna wśród <paramref name="aTaken"/>: aName, aName2, aName3…</summary>
    public static string Unique(IEnumerable<string> aTaken, string aName)
    {
        var taken = aTaken.ToHashSet();
        if (!taken.Contains(aName))
            return aName;
        for (var index = 2; ; index++)
            if (!taken.Contains(aName + index))
                return aName + index;
    }
}

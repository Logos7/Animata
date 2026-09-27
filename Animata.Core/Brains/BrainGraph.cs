using System.Numerics;
using Animata.Core.Brains.Modules;

namespace Animata.Core.Brains;

/// <summary>
/// Acykliczny graf modułów połączonych port→port.
/// Kompiluje się leniwie (sortowanie topologiczne + walidacja) i rekompiluje, gdy zmieni się lista
/// modułów lub połączeń. Po zmianie konfiguracji wewnątrz modułu (np. portów sieci) wołaj <see cref="Invalidate"/>.
/// </summary>
public class BrainGraph
{
    private CompiledGraph? _compiled;

    public List<BrainModule> Modules { get; } = [];
    public List<BrainConnection> Connections { get; } = [];

    /// <summary>Położenia węzłów w edytorze (lewy górny róg, jednostki edytora). Nie wpływa na działanie ani snapshoty.</summary>
    public Dictionary<Guid, Vector2> Positions { get; } = [];

    public TModule Add<TModule>(TModule aModule) where TModule : BrainModule
    {
        Modules.Add(aModule);
        return aModule;
    }

    public void Connect(BrainModule aSource, string aSourcePort, BrainModule aTarget, string aTargetPort) =>
        Connections.Add(new BrainConnection(aSource.Id, aSourcePort, aTarget.Id, aTargetPort));

    /// <summary>Łączy porty o tych samych nazwach.</summary>
    public void Connect(BrainModule aSource, BrainModule aTarget, params string[] aPorts)
    {
        foreach (var port in aPorts)
            Connect(aSource, port, aTarget, port);
    }

    /// <summary>Wymusza ponowną kompilację i walidację przy następnym użyciu.</summary>
    public void Invalidate() => _compiled = null;

    /// <summary>Jak <see cref="Invalidate"/>, także dla grafów wszystkich zagnieżdżonych podgrafów.</summary>
    public void InvalidateDeep()
    {
        _compiled = null;
        foreach (var composite in Modules.OfType<CompositeModule>())
            composite.Inner.InvalidateDeep();
    }

    /// <summary>Usuwa moduł razem z jego połączeniami i położeniem. Zwraca false, jeśli go nie było.</summary>
    public bool Remove(BrainModule aModule)
    {
        if (!Modules.Remove(aModule))
            return false;
        Connections.RemoveAll(aLink => aLink.SourceId == aModule.Id || aLink.TargetId == aModule.Id);
        Positions.Remove(aModule.Id);
        return true;
    }

    public bool Disconnect(BrainConnection aConnection) => Connections.Remove(aConnection);

    /// <summary>Moduł tego grafu (bez zagłębiania się w podgrafy) albo null.</summary>
    public BrainModule? Find(Guid aId) => Modules.Find(aModule => aModule.Id == aId);

    /// <summary>Moduł tego grafu albo dowolnego zagnieżdżonego podgrafu.</summary>
    public BrainModule? FindDeep(Guid aId) => Descendants().FirstOrDefault(aModule => aModule.Id == aId);

    /// <summary>Graf, w którym moduł bezpośrednio siedzi (ten albo zagnieżdżony), albo null.</summary>
    public BrainGraph? GraphOf(BrainModule aModule)
    {
        if (Modules.Contains(aModule))
            return this;
        foreach (var composite in Modules.OfType<CompositeModule>())
            if (composite.Inner.GraphOf(aModule) is { } graph)
                return graph;
        return null;
    }

    public bool ContainsDeep(BrainModule aModule) => GraphOf(aModule) is not null;

    /// <summary>Wszystkie moduły, z modułami podgrafów (w głąb, rodzic przed dziećmi).</summary>
    public IEnumerable<BrainModule> Descendants()
    {
        foreach (var module in Modules.ToArray())
        {
            yield return module;
            if (module is CompositeModule composite)
                foreach (var child in composite.Inner.Descendants())
                    yield return child;
        }
    }

    /// <summary>
    /// Wyjścia modułu z ostatniego Think (do podglądu wartości w edytorze) albo null, jeśli graf zmienił się od tamtej pory.
    /// Słownik jest ważny do następnego Think.
    /// </summary>
    public IReadOnlyDictionary<string, float>? LastOutputs(BrainModule aModule)
    {
        if (_compiled is not { } compiled || !Matches(compiled))
            return null;
        var index = Array.IndexOf(compiled.Modules, aModule);
        return index < 0 ? null : compiled.Outputs[index];
    }

    /// <summary>Kompiluje i waliduje graf. Rzuca <see cref="BrainException"/>, jeśli jest błędny.</summary>
    public void Validate() => EnsureCompiled();

    /// <summary>Czyści stan chwilowy wszystkich modułów (patrz <see cref="BrainModule.Reset"/>).</summary>
    public void Reset()
    {
        foreach (var module in Modules)
            module.Reset();
    }

    public void Think(BrainContext aContext)
    {
        var compiled = EnsureCompiled();
        Array.Clear(compiled.Outputs);

        foreach (var node in compiled.Nodes)
        {
            node.Inputs.Clear();
            foreach (var link in node.Incoming)
            {
                var source = compiled.Outputs[link.SourceIndex]
                    ?? throw new BrainException("Brain source was not evaluated.", node.Module.Id);
                if (!source.TryGetValue(link.SourcePort, out var value))
                    throw new BrainException(
                        $"{compiled.Modules[link.SourceIndex]} did not produce declared port {link.SourcePort}.",
                        compiled.Modules[link.SourceIndex].Id);
                node.Inputs.Add(link.TargetPort, value);
            }

            try
            {
                compiled.Outputs[node.Index] = node.Module.Evaluate(node.Inputs, aContext);
            }
            catch (Exception exception) when (exception is not BrainException)
            {
                throw new BrainException($"{node.Module} failed: {exception.Message}", node.Module.Id, exception);
            }
        }
    }

    public void Act(BrainContext aContext)
    {
        var compiled = EnsureCompiled();
        foreach (var node in compiled.Nodes)
        {
            try
            {
                node.Module.Commit(aContext);
            }
            catch (Exception exception) when (exception is not BrainException)
            {
                throw new BrainException($"{node.Module} failed to act: {exception.Message}", node.Module.Id, exception);
            }
        }
    }

    private CompiledGraph EnsureCompiled()
    {
        if (_compiled is null || !Matches(_compiled))
            _compiled = Compile();
        return _compiled;
    }

    private bool Matches(CompiledGraph aGraph)
    {
        if (Modules.Count != aGraph.Modules.Length || Connections.Count != aGraph.Connections.Length)
            return false;

        for (var i = 0; i < Modules.Count; i++)
            if (!ReferenceEquals(Modules[i], aGraph.Modules[i]))
                return false;

        for (var i = 0; i < Connections.Count; i++)
            if (Connections[i] != aGraph.Connections[i])
                return false;

        return true;
    }

    private CompiledGraph Compile()
    {
        var modules = Modules.ToArray();
        var connections = Connections.ToArray();
        var indices = new Dictionary<Guid, int>(modules.Length);

        for (var index = 0; index < modules.Length; index++)
        {
            var module = modules[index];
            if (!indices.TryAdd(module.Id, index))
                throw new BrainException($"Duplicate module id {module.Id}.", module.Id);
            try
            {
                module.Validate();
            }
            catch (Exception exception) when (exception is not BrainException)
            {
                throw new BrainException($"{module} is misconfigured: {exception.Message}", module.Id, exception);
            }
        }

        // Także w podgrafach: jeden aktuator może być sterowany tylko z jednego miejsca całego mózgu.
        var driven = Descendants().OfType<ActuatorModule>().GroupBy(aModule => aModule.ActuatorId)
            .FirstOrDefault(aGroup => aGroup.Count() > 1);
        if (driven is not null)
            throw new BrainException($"Actuator {driven.Key} is driven by more than one module.", driven.First().Id);

        var incoming = modules.Select(_ => new List<BrainConnection>()).ToArray();
        var outgoing = modules.Select(_ => new List<int>()).ToArray();

        foreach (var connection in connections)
        {
            if (!indices.TryGetValue(connection.SourceId, out var source) ||
                !indices.TryGetValue(connection.TargetId, out var target))
                throw new BrainException($"Connection {connection} references a missing module.");
            if (!modules[source].OutputPorts.Contains(connection.SourcePort))
                throw new BrainException(
                    $"{modules[source]} has no output port {connection.SourcePort}.", modules[source].Id);
            if (!modules[target].InputPorts.Contains(connection.TargetPort))
                throw new BrainException(
                    $"{modules[target]} has no input port {connection.TargetPort}.", modules[target].Id);
            if (incoming[target].Any(aExisting => aExisting.TargetPort == connection.TargetPort))
                throw new BrainException(
                    $"Multiple connections feed {modules[target]} port {connection.TargetPort}.", modules[target].Id);

            incoming[target].Add(connection);
            outgoing[source].Add(target);
        }

        var degrees = incoming.Select(aLinks => aLinks.Count).ToArray();
        var ready = new Queue<int>(Enumerable.Range(0, modules.Length).Where(aIndex => degrees[aIndex] == 0));
        var nodes = new List<CompiledNode>(modules.Length);

        while (ready.TryDequeue(out var index))
        {
            var links = incoming[index].Select(aLink =>
                new InputLink(indices[aLink.SourceId], aLink.SourcePort, aLink.TargetPort)).ToArray();
            nodes.Add(new CompiledNode(modules[index], index, links));
            foreach (var target in outgoing[index])
                if (--degrees[target] == 0)
                    ready.Enqueue(target);
        }

        if (nodes.Count != modules.Length)
            throw new BrainException("Brain graph contains a cycle.");

        return new CompiledGraph(modules, connections, nodes.ToArray(),
            new IReadOnlyDictionary<string, float>?[modules.Length]);
    }

    private sealed record CompiledGraph(
        BrainModule[] Modules,
        BrainConnection[] Connections,
        CompiledNode[] Nodes,
        IReadOnlyDictionary<string, float>?[] Outputs);

    private sealed class CompiledNode(BrainModule aModule, int aIndex, InputLink[] aIncoming)
    {
        public BrainModule Module { get; } = aModule;
        public int Index { get; } = aIndex;
        public InputLink[] Incoming { get; } = aIncoming;
        public Dictionary<string, float> Inputs { get; } = [];
    }

    private readonly record struct InputLink(int SourceIndex, string SourcePort, string TargetPort);
}

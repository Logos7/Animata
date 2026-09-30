namespace Animata.Core.Brains.Modules;

/// <summary>
/// Podgraf jako jeden węzeł (wzorzec kompozytu): wewnątrz jest zwykły <see cref="BrainGraph"/> z dwoma węzłami granic.
/// <see cref="Input"/> wystawia wejścia kompozytu jako swoje wyjścia, <see cref="Output"/> zbiera wyjścia kompozytu.
/// Porty kompozytu to więc porty granic; po ich zmianie (dodanie, usunięcie, zmiana nazwy) trzeba unieważnić graf
/// rodzica — najwygodniej przez <see cref="BrainGraphEditing"/>, które przy okazji poprawia połączenia w rodzicu.
/// Think kompozytu to Think grafu wnętrza, Reset — Reset wnętrza. Węzły ciała (zmysły i napędy) są tylko na najwyższym
/// poziomie mózgu, więc podgraf niczego nie zmienia w świecie — nie ma fazy Act.
/// Snapshot kompozytu to stany modułów wnętrza (po Id), rekurencyjnie; struktury (modułów i połączeń) nie zapisuje.
/// </summary>
public sealed class CompositeModule : BrainModule
{
    public CompositeModule(IEnumerable<string>? aInputs = null, IEnumerable<string>? aOutputs = null)
        : this(Guid.NewGuid(), Guid.NewGuid(), aInputs, aOutputs)
    {
    }

    /// <summary>Z zadanymi Id granic (wczytywanie zapisanego mózgu — połączenia wnętrza odwołują się do tych Id).</summary>
    public CompositeModule(Guid aInputId, Guid aOutputId, IEnumerable<string>? aInputs = null, IEnumerable<string>? aOutputs = null)
    {
        Name = "Podgraf";
        Input = Inner.Add(new SubgraphInputModule { Id = aInputId });
        Output = Inner.Add(new SubgraphOutputModule { Id = aOutputId });
        foreach (var port in aInputs ?? [])
            AddInputPort(port);
        foreach (var port in aOutputs ?? [])
            AddOutputPort(port);
    }

    public BrainGraph Inner { get; } = new();

    /// <summary>Granica wejść: wewnątrz podgrafu źródło wartości wejść kompozytu.</summary>
    public SubgraphInputModule Input { get; }

    /// <summary>Granica wyjść: wewnątrz podgrafu ujście, z którego kompozyt bierze swoje wyjścia.</summary>
    public SubgraphOutputModule Output { get; }

    public override IReadOnlyList<string> InputPorts => Input.OutputPorts;
    public override IReadOnlyList<string> OutputPorts => Output.InputPorts;

    public void AddInputPort(string aPort) => AddPort(Input.PortList, aPort);
    public void AddOutputPort(string aPort) => AddPort(Output.PortList, aPort);

    /// <summary>Usuwa port wejścia i połączenia wnętrza, które z niego wychodzą. Połączenia rodzica poprawia wołający.</summary>
    public bool RemoveInputPort(string aPort)
    {
        if (!Input.PortList.Remove(aPort))
            return false;
        Inner.Connections.RemoveAll(aLink => aLink.SourceId == Input.Id && aLink.SourcePort == aPort);
        Inner.Invalidate();
        return true;
    }

    /// <summary>Usuwa port wyjścia i połączenie wnętrza, które do niego prowadzi. Połączenia rodzica poprawia wołający.</summary>
    public bool RemoveOutputPort(string aPort)
    {
        if (!Output.PortList.Remove(aPort))
            return false;
        Inner.Connections.RemoveAll(aLink => aLink.TargetId == Output.Id && aLink.TargetPort == aPort);
        Inner.Invalidate();
        return true;
    }

    /// <summary>Zmienia nazwę portu wejścia razem z połączeniami wnętrza. Połączenia rodzica poprawia wołający.</summary>
    public void RenameInputPort(string aOld, string aNew)
    {
        Rename(Input.PortList, aOld, aNew);
        for (var i = 0; i < Inner.Connections.Count; i++)
            if (Inner.Connections[i].SourceId == Input.Id && Inner.Connections[i].SourcePort == aOld)
                Inner.Connections[i] = Inner.Connections[i] with { SourcePort = aNew };
        Inner.Invalidate();
    }

    /// <summary>Zmienia nazwę portu wyjścia razem z połączeniami wnętrza. Połączenia rodzica poprawia wołający.</summary>
    public void RenameOutputPort(string aOld, string aNew)
    {
        Rename(Output.PortList, aOld, aNew);
        for (var i = 0; i < Inner.Connections.Count; i++)
            if (Inner.Connections[i].TargetId == Output.Id && Inner.Connections[i].TargetPort == aOld)
                Inner.Connections[i] = Inner.Connections[i] with { TargetPort = aNew };
        Inner.Invalidate();
    }

    /// <summary>Moduły wnętrza bez granic.</summary>
    public IEnumerable<BrainModule> Children => Inner.Modules.Where(aModule => aModule != Input && aModule != Output);

    public override IReadOnlyDictionary<string, float> Evaluate(
        IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        Input.Load(aInputs);
        Inner.Think(aContext);
        return Output.Values;
    }

    public override void Reset() => Inner.Reset();

    public override void Validate()
    {
        if (Inner.Descendants().Contains(this))
            throw new InvalidOperationException("A composite cannot contain itself.");
        if (!Inner.Modules.Contains(Input) || !Inner.Modules.Contains(Output))
            throw new InvalidOperationException("Subgraph boundary modules are missing.");
        if (Inner.Modules.Count(aModule => aModule is SubgraphInputModule) != 1 ||
            Inner.Modules.Count(aModule => aModule is SubgraphOutputModule) != 1)
            throw new InvalidOperationException("A subgraph needs exactly one input and one output boundary.");
        if (Inner.Modules.FirstOrDefault(aModule => aModule is SensorModule or ActuatorModule) is { } bodyNode)
            throw new InvalidOperationException($"{bodyNode} belongs to the body — body nodes live only at the top level of a brain.");
        Inner.Validate();
    }

    public override ModuleState? CaptureState()
    {
        var entries = new List<ModuleSnapshot>();
        foreach (var module in Children)
            if (module.CaptureState() is { } state)
                entries.Add(new ModuleSnapshot(module.Id, module.Name, state));
        return entries.Count == 0 ? null : new CompositeState([.. entries]);
    }

    public override void RestoreState(ModuleState aState)
    {
        var state = Expect<CompositeState>(aState);
        foreach (var entry in state.Modules)
            Inner.FindDeep(entry.ModuleId)?.RestoreState(entry.State);
        Inner.InvalidateDeep();
    }

    private void AddPort(List<string> aPorts, string aPort)
    {
        if (string.IsNullOrWhiteSpace(aPort))
            throw new ArgumentException("Port name must not be empty.", nameof(aPort));
        if (aPorts.Contains(aPort))
            throw new ArgumentException($"Port {aPort} already exists.", nameof(aPort));
        aPorts.Add(aPort);
        Inner.Invalidate();
    }

    private static void Rename(List<string> aPorts, string aOld, string aNew)
    {
        var index = aPorts.IndexOf(aOld);
        if (index < 0)
            throw new ArgumentException($"Port {aOld} does not exist.", nameof(aOld));
        if (string.IsNullOrWhiteSpace(aNew))
            throw new ArgumentException("Port name must not be empty.", nameof(aNew));
        if (aOld != aNew && aPorts.Contains(aNew))
            throw new ArgumentException($"Port {aNew} already exists.", nameof(aNew));
        aPorts[index] = aNew;
    }
}

/// <summary>Granica wejść podgrafu: wewnątrz podgrafu wystawia wejścia kompozytu jako swoje wyjścia.</summary>
public sealed class SubgraphInputModule : BrainModule
{
    private readonly Dictionary<string, float> _values = [];

    internal SubgraphInputModule() => Name = "Wejście";

    internal List<string> PortList { get; } = [];

    public override IReadOnlyList<string> InputPorts => [];
    public override IReadOnlyList<string> OutputPorts => PortList;

    internal void Load(IReadOnlyDictionary<string, float> aInputs)
    {
        _values.Clear();
        foreach (var port in PortList)
            _values[port] = aInputs.GetValueOrDefault(port);
    }

    public override IReadOnlyDictionary<string, float> Evaluate(
        IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        foreach (var port in PortList)
            _values.TryAdd(port, 0);
        return _values;
    }
}

/// <summary>Granica wyjść podgrafu: zbiera wartości, które kompozyt wystawia na zewnątrz (nieprzyłączone = 0).</summary>
public sealed class SubgraphOutputModule : BrainModule
{
    private static readonly Dictionary<string, float> NoOutputs = [];
    private readonly Dictionary<string, float> _values = [];

    internal SubgraphOutputModule() => Name = "Wyjście";

    internal List<string> PortList { get; } = [];

    public override IReadOnlyList<string> InputPorts => PortList;
    public override IReadOnlyList<string> OutputPorts => [];

    /// <summary>Wyjścia kompozytu z ostatniego Think (wszystkie porty).</summary>
    public IReadOnlyDictionary<string, float> Values => _values;

    public override IReadOnlyDictionary<string, float> Evaluate(
        IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        _values.Clear();
        foreach (var port in PortList)
            _values[port] = aInputs.GetValueOrDefault(port);
        return NoOutputs;
    }
}

namespace Animata.Core.Brains.Modules;

/// <summary>
/// Przełącznik kanałów: ma N kanałów o tym samym zestawie portów i przepuszcza jeden z nich.
/// Wejścia: "Select" oraz "In{kanał}.{port}" (np. "In0.Turn", "In1.Turn").
/// Wyjścia: porty bez prefiksu (np. "Turn").
/// Kanał = Select zaokrąglone i obcięte do [0, N-1]. Nieprzyłączony port kanału daje 0.
/// </summary>
public sealed class RouterModule : BrainModule
{
    public const string SelectPort = "Select";

    private readonly string[] _ports;
    private readonly string[][] _channelPorts;
    private readonly string[] _inputPorts;
    private readonly Dictionary<string, float> _outputs = [];

    public RouterModule(int aChannels, params string[] aPorts)
    {
        if (aChannels < 1)
            throw new ArgumentOutOfRangeException(nameof(aChannels), "A router needs at least one channel.");
        if (aPorts.Length == 0 || aPorts.Distinct().Count() != aPorts.Length)
            throw new ArgumentException("Router ports must be non-empty and unique.", nameof(aPorts));

        _ports = (string[])aPorts.Clone();
        _channelPorts = Enumerable.Range(0, aChannels)
            .Select(aChannel => _ports.Select(aPort => ChannelPort(aChannel, aPort)).ToArray())
            .ToArray();
        _inputPorts = [SelectPort, .. _channelPorts.SelectMany(aChannel => aChannel)];
        foreach (var port in _ports)
            _outputs[port] = 0;
    }

    public int Channels => _channelPorts.Length;

    /// <summary>Porty jednego kanału (i zarazem wyjścia).</summary>
    public IReadOnlyList<string> Ports => _ports;

    /// <summary>Kanał wybrany w ostatnim Evaluate (do podglądu).</summary>
    public int ActiveChannel { get; private set; }

    public override IReadOnlyList<string> InputPorts => _inputPorts;
    public override IReadOnlyList<string> OutputPorts => _ports;

    public static string ChannelPort(int aChannel, string aPort) => $"In{aChannel}.{aPort}";

    /// <summary>Konfiguracja routera (zapis w pliku, snapshot).</summary>
    public override ModuleState CaptureState() => new RouterState(Channels, [.. _ports]);

    /// <summary>Konfiguracji routera nie da się zmienić — stan musi być ten sam, inaczej <see cref="ArgumentException"/>.</summary>
    public override void RestoreState(ModuleState aState)
    {
        var state = Expect<RouterState>(aState);
        if (state.Channels != Channels || !state.Ports.SequenceEqual(_ports))
            throw new ArgumentException("Router ma inną liczbę kanałów albo inne porty niż w stanie.", nameof(aState));
    }

    public override IReadOnlyDictionary<string, float> Evaluate(
        IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        var select = aInputs.GetValueOrDefault(SelectPort);
        if (!float.IsFinite(select))
            throw new InvalidOperationException("Router select is not finite.");

        ActiveChannel = (int)Math.Clamp(MathF.Round(select), 0, Channels - 1);
        var channel = _channelPorts[ActiveChannel];
        for (var index = 0; index < _ports.Length; index++)
            _outputs[_ports[index]] = aInputs.GetValueOrDefault(channel[index]);
        return _outputs;
    }
}

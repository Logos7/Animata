using Animata.Core.Brains.Neural;

namespace Animata.Core.Brains.Modules;

/// <summary>
/// Przejście między stanami: z <see cref="From"/> do <see cref="To"/>, gdy wyrażenie <see cref="Condition"/> (nad portami
/// warunków automatu, np. „Found * Gap”) jest większe (<see cref="Above"/>) albo mniejsze od <see cref="Threshold"/>.
/// </summary>
public sealed record StateTransition(string From, string To, string Condition, bool Above, float Threshold)
{
    public override string ToString() => $"{From} → {To}, gdy {Condition} {(Above ? ">" : "<")} {Threshold:0.###}";
}

/// <summary>
/// Automat stanów — mózg, który robi co innego w zależności od sytuacji (np. stoi w miejscu albo idzie do celu).
/// Każdy stan ma własne wejścia na te same porty wyjściowe: „{stan}.{port}” (np. „Stoję.Yaw0”, „Idę.Yaw0”) — zwykle
/// podpięte do osobnych sieci. Wyjście portu to mieszanka wejść stanów z wagami: waga bieżącego stanu rośnie do 1
/// w czasie <see cref="BlendSeconds"/>, pozostałych maleje do 0 (płynne przełączanie, bez szarpnięcia stawów).
/// Przejścia (<see cref="Transitions"/>) sprawdza w kolejności; pierwsze spełnione z bieżącego stanu przełącza — ale nie
/// wcześniej niż po <see cref="MinDwellSeconds"/> w stanie (bez migotania na granicy progu). Dodatkowe wyjście „State” to
/// numer bieżącego stanu (do podglądu albo dla dalszych modułów).
/// Stan chwilowy (bieżący stan, wagi, czas w stanie) czyści <see cref="Reset"/> — automat startuje w pierwszym stanie.
/// </summary>
public sealed class StateMachineModule : BrainModule
{
    public const string StatePort = "State";

    private readonly string[] _states;
    private readonly string[] _conditions;
    private readonly string[] _ports;
    private readonly string[] _inputPorts;
    private readonly string[] _outputPorts;
    private readonly string[][] _statePorts;
    private readonly Dictionary<string, float> _outputs = [];
    private readonly Dictionary<string, float> _variables = [];
    private StateTransition[] _transitions = [];
    private (int From, int To, CompiledExpression Condition, bool Above, float Threshold)[] _compiled = [];
    private readonly float[] _weights;
    private int _current;
    private float _dwell;

    public StateMachineModule(IReadOnlyList<string> aStates, IReadOnlyList<string> aConditions, IReadOnlyList<string> aPorts,
        IEnumerable<StateTransition>? aTransitions = null)
    {
        if (aStates.Count < 1 || aStates.Distinct().Count() != aStates.Count || aStates.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Automat potrzebuje co najmniej jednego stanu; nazwy niepuste i różne.", nameof(aStates));
        if (aPorts.Count < 1 || aPorts.Distinct().Count() != aPorts.Count || aPorts.Contains(StatePort))
            throw new ArgumentException($"Porty automatu muszą być niepuste, różne i inne niż „{StatePort}”.", nameof(aPorts));
        if (aConditions.Distinct().Count() != aConditions.Count || aConditions.Any(aPort => aPort.Contains('.')))
            throw new ArgumentException("Porty warunków muszą być różne i bez kropki.", nameof(aConditions));
        _states = [.. aStates];
        _conditions = [.. aConditions];
        _ports = [.. aPorts];
        _statePorts = [.. _states.Select(aState => _ports.Select(aPort => StatePortName(aState, aPort)).ToArray())];
        _inputPorts = [.. _conditions, .. _statePorts.SelectMany(aPorts => aPorts)];
        _outputPorts = [.. _ports, StatePort];
        _weights = new float[_states.Length];
        foreach (var port in _outputPorts)
            _outputs[port] = 0;
        SetTransitions(aTransitions ?? []);
        Reset();
    }

    public IReadOnlyList<string> States => _states;
    public IReadOnlyList<string> Conditions => _conditions;
    public IReadOnlyList<string> Ports => _ports;
    public IReadOnlyList<StateTransition> Transitions => _transitions;

    /// <summary>Czas płynnego przejścia (s); 0 — przełączenie natychmiast.</summary>
    public float BlendSeconds { get; set; } = 0.4f;

    /// <summary>Najkrótszy pobyt w stanie przed kolejnym przejściem (s).</summary>
    public float MinDwellSeconds { get; set; } = 1;

    /// <summary>Bieżący stan (numer i nazwa) — po ostatnim Evaluate.</summary>
    public int Current => _current;
    public string CurrentName => _states[_current];

    /// <summary>Wagi stanów w mieszance wyjść (suma 1).</summary>
    public IReadOnlyList<float> Weights => _weights;

    public override IReadOnlyList<string> InputPorts => _inputPorts;
    public override IReadOnlyList<string> OutputPorts => _outputPorts;

    public static string StatePortName(string aState, string aPort) => $"{aState}.{aPort}";

    /// <summary>Nowe przejścia; stany i zmienne warunków muszą istnieć (<see cref="ArgumentException"/> albo <see cref="FormatException"/>).</summary>
    public void SetTransitions(IEnumerable<StateTransition> aTransitions)
    {
        var transitions = aTransitions.ToArray();
        var compiled = new (int, int, CompiledExpression, bool, float)[transitions.Length];
        for (var index = 0; index < transitions.Length; index++)
        {
            var transition = transitions[index];
            var from = Array.IndexOf(_states, transition.From);
            var to = Array.IndexOf(_states, transition.To);
            if (from < 0 || to < 0)
                throw new ArgumentException($"Przejście {transition}: nie ma takiego stanu.");
            var condition = SensorExpression.Compile(transition.Condition);
            if (condition.Variables.FirstOrDefault(aVariable => !_conditions.Contains(aVariable)) is { } unknown)
                throw new ArgumentException($"Przejście {transition}: „{unknown}” nie jest portem warunku ({string.Join(", ", _conditions)}).");
            if (!float.IsFinite(transition.Threshold))
                throw new ArgumentException($"Przejście {transition}: próg musi być liczbą.");
            compiled[index] = (from, to, condition, transition.Above, transition.Threshold);
        }
        _transitions = transitions;
        _compiled = compiled;
    }

    public override IReadOnlyDictionary<string, float> Evaluate(IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        var delta = Math.Max(0, aContext.Delta);
        _dwell += delta;
        if (_dwell >= MinDwellSeconds)
        {
            foreach (var condition in _conditions)
                _variables[condition] = aInputs.GetValueOrDefault(condition);
            foreach (var (from, to, expression, above, threshold) in _compiled)
            {
                if (from != _current || to == _current)
                    continue;
                var value = expression.Evaluate(_variables);
                if (above ? value > threshold : value < threshold)
                {
                    _current = to;
                    _dwell = 0;
                    break;
                }
            }
        }

        var step = BlendSeconds > 0 ? delta / BlendSeconds : 1;
        var total = 0f;
        for (var state = 0; state < _weights.Length; state++)
        {
            _weights[state] = state == _current ? Math.Min(1, _weights[state] + step) : Math.Max(0, _weights[state] - step);
            total += _weights[state];
        }
        for (var state = 0; state < _weights.Length; state++)
            _weights[state] = total > 0 ? _weights[state] / total : state == _current ? 1 : 0;

        for (var port = 0; port < _ports.Length; port++)
        {
            var value = 0f;
            for (var state = 0; state < _states.Length; state++)
                if (_weights[state] > 0)
                    value += _weights[state] * aInputs.GetValueOrDefault(_statePorts[state][port]);
            _outputs[_ports[port]] = value;
        }
        _outputs[StatePort] = _current;
        return _outputs;
    }

    /// <summary>Pierwszy stan, pełna waga, może od razu przejść dalej.</summary>
    public override void Reset()
    {
        _current = 0;
        Array.Clear(_weights);
        _weights[0] = 1;
        _dwell = MinDwellSeconds;
    }

    public override ModuleState CaptureState() =>
        new StateMachineState([.. _states], [.. _conditions], [.. _ports], [.. _transitions], BlendSeconds, MinDwellSeconds);

    /// <summary>Przejścia i czasy ze stanu; stany, warunki i porty muszą być te same (inaczej <see cref="ArgumentException"/>).</summary>
    public override void RestoreState(ModuleState aState)
    {
        var state = Expect<StateMachineState>(aState);
        if (!state.States.SequenceEqual(_states) || !state.Conditions.SequenceEqual(_conditions) || !state.Ports.SequenceEqual(_ports))
            throw new ArgumentException("Automat ma inne stany, warunki albo porty niż w stanie.", nameof(aState));
        SetTransitions(state.Transitions);
        BlendSeconds = Math.Max(0, state.BlendSeconds);
        MinDwellSeconds = Math.Max(0, state.MinDwellSeconds);
    }
}

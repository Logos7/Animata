using Animata.Core.Brains.Neural;

namespace Animata.Core.Brains.Modules;

/// <summary>
/// Sieć neuronowa jako czysta funkcja portów.
/// Wejścia modułu: nazwy z <see cref="Ports"/>; każde wejście sieci to wyrażenie nad tymi nazwami.
/// Wyjścia modułu: porty z <see cref="Outputs"/> (sygnał tanh * Scale + Offset).
/// Po zmianie Ports/Inputs/Outputs w podpiętym grafie wołaj BrainGraph.Invalidate().
/// </summary>
public sealed class NeuralNetworkModule : BrainModule, ITrainableModule
{
    private readonly Dictionary<string, float> _variables = [];
    private readonly Dictionary<string, float> _results = [];
    private float[] _inputValues = [];

    public NeuralNetworkModule(NeuralNetwork aNetwork) => Network = aNetwork;

    public NeuralNetwork Network { get; }

    public int ParameterCount => Network.ParameterCount;

    public float[] GetParameters() => Network.GetParameters();

    public void SetParameters(ReadOnlySpan<float> aParameters) => Network.SetParameters(aParameters);

    public void Randomize(Random? aRandom = null) => Network.Randomize(aRandom);

    /// <summary>Nazwy portów wejściowych modułu (zmienne dostępne w wyrażeniach).</summary>
    public List<string> Ports { get; } = [];

    public List<NeuralInput> Inputs { get; } = [];
    public List<NeuralOutput> Outputs { get; } = [];

    public override IReadOnlyList<string> InputPorts => Ports;
    public override IReadOnlyList<string> OutputPorts => Outputs.Select(aOutput => aOutput.Port).ToArray();

    public override ModuleState CaptureState() => new NeuralNetworkState(
        Network.Layers.ToArray(),
        Network.CopyWeights(),
        Network.CopyBiases(),
        Ports.ToArray(),
        Inputs.Select(aInput => aInput.Expression).ToArray(),
        Outputs.ToArray());

    public override void RestoreState(ModuleState aState)
    {
        var state = Expect<NeuralNetworkState>(aState);

        // Najpierw wszystko, co może rzucić (kompilacja wyrażeń, kształt wag), dopiero potem podmiana.
        var inputs = CompileInputs(state);
        Network.Load(state.Layers, state.Weights, state.Biases);
        SetBindings(state, inputs);
    }

    /// <summary>
    /// Nowy moduł o kształcie sieci i powiązaniach portów z <paramref name="aShape"/> (jego wagi są pomijane)
    /// i podanych parametrach (kolejność jak <see cref="NeuralNetwork.GetParameters"/>).
    /// </summary>
    public static NeuralNetworkModule Create(NeuralNetworkState aShape, ReadOnlySpan<float> aParameters, string aName = "Neural")
    {
        var inputs = CompileInputs(aShape);
        var module = new NeuralNetworkModule(new NeuralNetwork(aShape.Layers)) { Name = aName };
        module.Network.SetParameters(aParameters);
        module.SetBindings(aShape, inputs);
        return module;
    }

    private static NeuralInput[] CompileInputs(NeuralNetworkState aState) =>
        aState.InputExpressions.Select(aExpression => new NeuralInput(aExpression)).ToArray();

    private void SetBindings(NeuralNetworkState aState, NeuralInput[] aInputs)
    {
        Ports.Clear();
        Ports.AddRange(aState.Ports);
        Inputs.Clear();
        Inputs.AddRange(aInputs);
        Outputs.Clear();
        Outputs.AddRange(aState.Outputs);
    }

    public override void Validate()
    {
        if (Inputs.Count != Network.Layers[0])
            throw new InvalidOperationException(
                $"Network expects {Network.Layers[0]} inputs but {Inputs.Count} are bound.");
        if (Outputs.Count != Network.Layers[^1])
            throw new InvalidOperationException(
                $"Network produces {Network.Layers[^1]} outputs but {Outputs.Count} are bound.");
        if (Ports.Any(string.IsNullOrWhiteSpace) || Ports.Distinct().Count() != Ports.Count)
            throw new InvalidOperationException("Input ports must be non-empty and unique.");
        if (Outputs.Any(aOutput => string.IsNullOrWhiteSpace(aOutput.Port)) ||
            Outputs.Select(aOutput => aOutput.Port).Distinct().Count() != Outputs.Count)
            throw new InvalidOperationException("Output ports must be non-empty and unique.");
        if (Outputs.Any(aOutput => !float.IsFinite(aOutput.Scale) || !float.IsFinite(aOutput.Offset)))
            throw new InvalidOperationException("Output scale and offset must be finite.");

        for (var index = 0; index < Inputs.Count; index++)
        {
            var unknown = Inputs[index].Variables.Where(aName => !Ports.Contains(aName)).ToArray();
            if (unknown.Length > 0)
                throw new InvalidOperationException(
                    $"Input {index} ({Inputs[index].Expression}) uses unknown ports: {string.Join(", ", unknown)}.");
        }
    }

    public override IReadOnlyDictionary<string, float> Evaluate(
        IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        if (Inputs.Count != Network.Layers[0] || Outputs.Count != Network.Layers[^1])
            throw new InvalidOperationException("Neural bindings no longer match the network layers.");

        _variables.Clear();
        foreach (var port in Ports)
            _variables[port] = aInputs.GetValueOrDefault(port);

        if (_inputValues.Length != Inputs.Count)
            _inputValues = new float[Inputs.Count];
        for (var index = 0; index < Inputs.Count; index++)
        {
            var value = Inputs[index].Evaluate(_variables);
            if (!float.IsFinite(value))
                throw new InvalidOperationException($"Input {index} ({Inputs[index].Expression}) is not finite.");
            _inputValues[index] = value;
        }

        var signals = Network.Evaluate(_inputValues);
        _results.Clear();
        for (var index = 0; index < Outputs.Count; index++)
        {
            var output = Outputs[index];
            _results[output.Port] = signals[index] * output.Scale + output.Offset;
        }
        return _results;
    }
}

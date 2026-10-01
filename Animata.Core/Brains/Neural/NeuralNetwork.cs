namespace Animata.Core.Brains.Neural;

public sealed class NeuralNetwork
{
    private int[] _layers = [];
    private float[][][] _weights = [];
    private float[][] _biases = [];
    private float[][] _values = [];

    public IReadOnlyList<int> Layers => _layers;
    public float[][][] Weights => _weights;
    public float[][] Biases => _biases;

    public NeuralNetwork(params int[] aLayers) => Resize(aLayers);

    private void Resize(params int[] aLayers)
    {
        if (aLayers.Length < 2 || aLayers.Any(aSize => aSize <= 0))
            throw new ArgumentException("A network needs at least two positive layer sizes.", nameof(aLayers));

        var weights = new float[aLayers.Length - 1][][];
        var biases = new float[aLayers.Length - 1][];
        var values = new float[aLayers.Length][];
        for (var layer = 0; layer < aLayers.Length; layer++)
            values[layer] = new float[aLayers[layer]];

        for (var layer = 1; layer < aLayers.Length; layer++)
        {
            weights[layer - 1] = new float[aLayers[layer]][];
            biases[layer - 1] = new float[aLayers[layer]];
            for (var neuron = 0; neuron < aLayers[layer]; neuron++)
            {
                weights[layer - 1][neuron] = new float[aLayers[layer - 1]];
                var scale = 1f / MathF.Sqrt(aLayers[layer - 1]);
                for (var source = 0; source < aLayers[layer - 1]; source++)
                    weights[layer - 1][neuron][source] =
                        layer - 1 < _weights.Length &&
                        neuron < _weights[layer - 1].Length &&
                        source < _weights[layer - 1][neuron].Length
                            ? _weights[layer - 1][neuron][source]
                            : (Random.Shared.NextSingle() * 2 - 1) * scale;
                if (layer - 1 < _biases.Length && neuron < _biases[layer - 1].Length)
                    biases[layer - 1][neuron] = _biases[layer - 1][neuron];
            }
        }

        _layers = (int[])aLayers.Clone();
        _weights = weights;
        _biases = biases;
        _values = values;
    }

    public ReadOnlySpan<float> Evaluate(ReadOnlySpan<float> aInputs)
    {
        if (aInputs.Length != _layers[0])
            throw new ArgumentException("Input count differs from the first layer.", nameof(aInputs));
        aInputs.CopyTo(_values[0]);
        for (var layer = 1; layer < _layers.Length; layer++)
            for (var neuron = 0; neuron < _layers[layer]; neuron++)
            {
                var sum = _biases[layer - 1][neuron];
                for (var source = 0; source < _layers[layer - 1]; source++)
                    sum += _weights[layer - 1][neuron][source] * _values[layer - 1][source];
                _values[layer][neuron] = MathF.Tanh(sum);
            }
        return _values[^1];
    }

    // ---------- edycja kształtu ----------

    /// <summary>Najmniej i najwięcej neuronów w warstwie ukrytej; najwięcej warstw ukrytych.</summary>
    public const int MinLayerSize = 1;
    public const int MaxLayerSize = 64;
    public const int MaxHiddenLayers = 8;

    /// <summary>Aktywacje warstwy z ostatniego <see cref="Evaluate"/> (0 przed pierwszym) — do podglądu.</summary>
    public ReadOnlySpan<float> Activations(int aLayer) => _values[aLayer];

    /// <summary>
    /// Nowa liczba neuronów warstwy ukrytej (1…<see cref="Layers"/>.Count − 2). Zostające neurony zachowują swoje wagi
    /// i biasy; nowe dostają losowe wagi (±1/√wejść) i bias 0, a następna warstwa losowe wagi od nich.
    /// </summary>
    public void ResizeLayer(int aLayer, int aSize)
    {
        EnsureHidden(aLayer);
        EnsureSize(aSize);
        var layers = (int[])_layers.Clone();
        layers[aLayer] = aSize;
        Resize(layers);
    }

    /// <summary>
    /// Wstawia warstwę ukrytą przed warstwą <paramref name="aIndex"/> (1…Count − 1). Wagi pozostałych par warstw zostają;
    /// nowe połączenia (do nowej warstwy i z niej) są losowe — sieć zmienia zachowanie i warto ją douczyć.
    /// </summary>
    public void InsertLayer(int aIndex, int aSize)
    {
        if (aIndex < 1 || aIndex > _layers.Length - 1)
            throw new ArgumentOutOfRangeException(nameof(aIndex), aIndex, "A hidden layer goes between the input and the output.");
        EnsureSize(aSize);
        if (_layers.Length - 2 >= MaxHiddenLayers)
            throw new InvalidOperationException($"A network has at most {MaxHiddenLayers} hidden layers.");
        var layers = _layers.ToList();
        layers.Insert(aIndex, aSize);
        // Macierze: przed nową warstwą bez zmian, dwie wokół niej nowe, dalsze przesunięte o jedną.
        var keep = new (float[][] Weights, float[] Biases)?[layers.Count - 1];
        for (var matrix = 0; matrix < _weights.Length; matrix++)
        {
            if (matrix < aIndex - 1)
                keep[matrix] = (_weights[matrix], _biases[matrix]);
            else if (matrix > aIndex - 1)
                keep[matrix + 1] = (_weights[matrix], _biases[matrix]);
        }
        Rebuild([.. layers], keep);
    }

    /// <summary>Usuwa warstwę ukrytą; warstwy po obu stronach łączą się nowymi, losowymi wagami.</summary>
    public void RemoveLayer(int aIndex)
    {
        EnsureHidden(aIndex);
        var layers = _layers.ToList();
        layers.RemoveAt(aIndex);
        var keep = new (float[][] Weights, float[] Biases)?[layers.Count - 1];
        for (var matrix = 0; matrix < _weights.Length; matrix++)
        {
            if (matrix < aIndex - 1)
                keep[matrix] = (_weights[matrix], _biases[matrix]);
            else if (matrix > aIndex)
                keep[matrix - 1] = (_weights[matrix], _biases[matrix]);
        }
        Rebuild([.. layers], keep);
    }

    private void EnsureHidden(int aLayer)
    {
        if (aLayer < 1 || aLayer > _layers.Length - 2)
            throw new ArgumentOutOfRangeException(nameof(aLayer), aLayer, "Only hidden layers can be changed (inputs and outputs follow the ports).");
    }

    private static void EnsureSize(int aSize)
    {
        if (aSize < MinLayerSize || aSize > MaxLayerSize)
            throw new ArgumentOutOfRangeException(nameof(aSize), aSize, $"A layer has {MinLayerSize}–{MaxLayerSize} neurons.");
    }

    /// <summary>Nowy kształt: macierze z <paramref name="aKeep"/> przepisane, reszta losowa (±1/√wejść), biasy nowych 0.</summary>
    private void Rebuild(int[] aLayers, (float[][] Weights, float[] Biases)?[] aKeep)
    {
        var weights = new float[aLayers.Length - 1][][];
        var biases = new float[aLayers.Length - 1][];
        for (var matrix = 0; matrix < weights.Length; matrix++)
        {
            if (aKeep[matrix] is { } kept)
            {
                weights[matrix] = kept.Weights;
                biases[matrix] = kept.Biases;
                continue;
            }
            var scale = 1f / MathF.Sqrt(aLayers[matrix]);
            weights[matrix] = new float[aLayers[matrix + 1]][];
            biases[matrix] = new float[aLayers[matrix + 1]];
            for (var neuron = 0; neuron < aLayers[matrix + 1]; neuron++)
            {
                weights[matrix][neuron] = new float[aLayers[matrix]];
                for (var source = 0; source < aLayers[matrix]; source++)
                    weights[matrix][neuron][source] = (Random.Shared.NextSingle() * 2 - 1) * scale;
            }
        }
        _layers = aLayers;
        _weights = weights;
        _biases = biases;
        _values = aLayers.Select(aSize => new float[aSize]).ToArray();
    }

    /// <summary>Liczba uczonych parametrów: wszystkie wagi i biasy.</summary>
    public int ParameterCount
    {
        get
        {
            var count = 0;
            for (var layer = 1; layer < _layers.Length; layer++)
                count += _layers[layer] * (_layers[layer - 1] + 1);
            return count;
        }
    }

    /// <summary>
    /// Spłaszczone parametry w stałej kolejności: warstwa → neuron → (wagi od źródeł, potem bias).
    /// </summary>
    public float[] GetParameters()
    {
        var parameters = new float[ParameterCount];
        var index = 0;
        for (var layer = 0; layer < _weights.Length; layer++)
            for (var neuron = 0; neuron < _weights[layer].Length; neuron++)
            {
                _weights[layer][neuron].CopyTo(parameters, index);
                index += _weights[layer][neuron].Length;
                parameters[index++] = _biases[layer][neuron];
            }
        return parameters;
    }

    public void SetParameters(ReadOnlySpan<float> aParameters)
    {
        if (aParameters.Length != ParameterCount)
            throw new ArgumentException($"Expected {ParameterCount} parameters, got {aParameters.Length}.", nameof(aParameters));
        var index = 0;
        for (var layer = 0; layer < _weights.Length; layer++)
            for (var neuron = 0; neuron < _weights[layer].Length; neuron++)
            {
                var weights = _weights[layer][neuron];
                aParameters.Slice(index, weights.Length).CopyTo(weights);
                index += weights.Length;
                _biases[layer][neuron] = aParameters[index++];
            }
    }

    /// <summary>Losowe wagi (±1/√wejść) i zerowe biasy.</summary>
    public void Randomize(Random? aRandom = null)
    {
        var random = aRandom ?? Random.Shared;
        for (var layer = 0; layer < _weights.Length; layer++)
        {
            var scale = 1f / MathF.Sqrt(_layers[layer]);
            for (var neuron = 0; neuron < _weights[layer].Length; neuron++)
            {
                var weights = _weights[layer][neuron];
                for (var source = 0; source < weights.Length; source++)
                    weights[source] = (random.NextSingle() * 2 - 1) * scale;
                _biases[layer][neuron] = 0;
            }
        }
    }

    /// <summary>Głęboka kopia wag.</summary>
    public float[][][] CopyWeights() =>
        _weights.Select(aLayer => aLayer.Select(aNeuron => (float[])aNeuron.Clone()).ToArray()).ToArray();

    /// <summary>Głęboka kopia biasów.</summary>
    public float[][] CopyBiases() => _biases.Select(aLayer => (float[])aLayer.Clone()).ToArray();

    /// <summary>Ustawia kształt i kopiuje wagi oraz biasy; kształty muszą się zgadzać z warstwami.</summary>
    public void Load(IReadOnlyList<int> aLayers, float[][][] aWeights, float[][] aBiases)
    {
        var layers = aLayers.ToArray();
        if (aWeights.Length != layers.Length - 1 || aBiases.Length != layers.Length - 1)
            throw new ArgumentException("Weights and biases must have one entry per non-input layer.");
        for (var layer = 1; layer < layers.Length; layer++)
        {
            if (aWeights[layer - 1].Length != layers[layer] || aBiases[layer - 1].Length != layers[layer] ||
                aWeights[layer - 1].Any(aNeuron => aNeuron.Length != layers[layer - 1]))
                throw new ArgumentException($"Layer {layer} shape does not match {layers[layer - 1]}→{layers[layer]}.");
        }

        Resize(layers);
        for (var layer = 0; layer < _weights.Length; layer++)
            for (var neuron = 0; neuron < _weights[layer].Length; neuron++)
            {
                aWeights[layer][neuron].CopyTo(_weights[layer][neuron], 0);
                _biases[layer][neuron] = aBiases[layer][neuron];
            }
    }
}

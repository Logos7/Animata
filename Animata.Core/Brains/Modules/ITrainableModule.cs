namespace Animata.Core.Brains.Modules;

/// <summary>
/// Moduł, którego parametry uczy ewolucja (<c>TrainingController</c>): wektor liczb, który da się odczytać,
/// wpisać i wylosować. Sieć neuronowa (wagi i biasy) i CPG węża (amplituda, częstotliwość…).
/// Kształt modułu (porty, liczba wejść) nie jest parametrem — opisuje go <see cref="BrainModule.CaptureState"/>.
/// </summary>
public interface ITrainableModule
{
    int ParameterCount { get; }

    float[] GetParameters();

    void SetParameters(ReadOnlySpan<float> aParameters);

    void Randomize(Random? aRandom = null);
}

/// <summary>Budowa uczonego modułu ze stanu (kształt) i wektora parametrów — w ukrytych próbach nauki.</summary>
public static class TrainableModules
{
    public static int ParameterCount(ModuleState aShape) => aShape switch
    {
        NeuralNetworkState network => network.ParameterCount,
        CpgState => CpgModule.Parameters,
        _ => throw new NotSupportedException($"{aShape.GetType().Name} is not a trainable module state.")
    };

    public static BrainModule Create(ModuleState aShape, ReadOnlySpan<float> aParameters, string aName) => aShape switch
    {
        NeuralNetworkState network => NeuralNetworkModule.Create(network, aParameters, aName),
        CpgState cpg => CpgModule.Create(cpg, aParameters, aName),
        _ => throw new NotSupportedException($"{aShape.GetType().Name} is not a trainable module state.")
    };
}

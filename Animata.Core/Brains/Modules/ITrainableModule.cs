namespace Animata.Core.Brains.Modules;

/// <summary>
/// Moduł, którego parametry uczy ewolucja (<c>TrainingController</c>): wektor liczb, który da się odczytać,
/// wpisać i wylosować. Sieć neuronowa (wagi i biasy), CPG węża, generatory chodu, regulatory.
/// Kształt modułu (porty, liczba wejść) nie jest parametrem — opisuje go <see cref="BrainModule.CaptureState"/>;
/// moduł o danym kształcie i parametrach buduje <see cref="ModuleState.CreateTrainable"/>.
/// </summary>
public interface ITrainableModule
{
    float[] GetParameters();

    void SetParameters(ReadOnlySpan<float> aParameters);

    void Randomize(Random? aRandom = null);
}

/// <summary>
/// Uczony parametr modułu (<see cref="ParametricModule"/>): nazwa, zakres, do którego wartość jest przycinana
/// (<see cref="Min"/>–<see cref="Max"/>), zakres losowania (<see cref="RandomMin"/>–<see cref="RandomMax"/>)
/// i wartość zamiast liczby nieskończonej albo NaN (<see cref="NotFinite"/>; brak — <see cref="Min"/>).
/// </summary>
public readonly record struct ParameterSpec(string Name, float Min, float Max, float RandomMin, float RandomMax, float? NotFinite = null)
{
    /// <summary>Wartość przycięta do zakresu (ewolucja operuje na surowych liczbach).</summary>
    public float Clean(float aValue) => float.IsFinite(aValue) ? Math.Clamp(aValue, Min, Max) : NotFinite ?? Min;

    /// <summary>Ten sam zakres przycinania i losowania dla wielu parametrów (np. wzmocnień regulatora).</summary>
    public static ParameterSpec[] Same(float aMin, float aMax, float aRandomMin, float aRandomMax, float? aNotFinite, params string[] aNames) =>
        [.. aNames.Select(aName => new ParameterSpec(aName, aMin, aMax, aRandomMin, aRandomMax, aNotFinite))];
}

/// <summary>
/// Moduł z wektorem uczonych parametrów opisanych tabelą (<see cref="ParameterSpec"/>): odczyt, wpis z przycięciem
/// i losowanie robi baza — klasa pochodna podaje tabelę, wartości domyślne, <see cref="BrainModule.Evaluate"/> i rekord stanu.
/// Wartości domyślne i ustawiane przez właściwości modułu nie są przycinane (przycina <see cref="SetParameters"/>).
/// </summary>
public abstract class ParametricModule : BrainModule, ITrainableModule
{
    private readonly IReadOnlyList<ParameterSpec> _specs;

    protected ParametricModule(IReadOnlyList<ParameterSpec> aSpecs, IReadOnlyList<float> aDefaults)
    {
        if (aDefaults.Count != aSpecs.Count)
            throw new ArgumentException($"{aSpecs.Count} parameters, but {aDefaults.Count} defaults.", nameof(aDefaults));
        _specs = aSpecs;
        Values = [.. aDefaults];
    }

    /// <summary>Opis parametrów w kolejności wektora.</summary>
    public IReadOnlyList<ParameterSpec> ParameterSpecs => _specs;

    /// <summary>Bieżące parametry (indeksy jak w <see cref="ParameterSpecs"/>).</summary>
    protected float[] Values { get; }

    public float[] GetParameters() => [.. Values];

    public void SetParameters(ReadOnlySpan<float> aParameters)
    {
        if (aParameters.Length != _specs.Count)
            throw new ArgumentException($"Expected {_specs.Count} parameters, got {aParameters.Length}.", nameof(aParameters));
        for (var index = 0; index < _specs.Count; index++)
            Values[index] = _specs[index].Clean(aParameters[index]);
    }

    /// <summary>Każdy parametr losowany równomiernie z jego zakresu losowania, po kolei.</summary>
    public virtual void Randomize(Random? aRandom = null)
    {
        var random = aRandom ?? Random.Shared;
        var values = new float[_specs.Count];
        for (var index = 0; index < values.Length; index++)
            values[index] = _specs[index].RandomMin + random.NextSingle() * (_specs[index].RandomMax - _specs[index].RandomMin);
        SetParameters(values);
    }
}

/// <summary>Wpis parametrów do modułu przy budowie (np. ręcznie dobranych): <c>new BalanceModule().WithParameters(…)</c>.</summary>
public static class TrainableModuleExtensions
{
    public static TModule WithParameters<TModule>(this TModule aModule, ReadOnlySpan<float> aParameters) where TModule : ITrainableModule
    {
        aModule.SetParameters(aParameters);
        return aModule;
    }
}

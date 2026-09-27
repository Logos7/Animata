using System.Text.Json;
using System.Text.Json.Serialization;
using Animata.Core.Brains.Neural;

namespace Animata.Core.Brains;

/// <summary>
/// Serializowalny stan modułu: jego parametry i konfiguracja (nie stan chwilowy z ticku).
/// Każdy typ modułu, który ma coś do zapamiętania, definiuje własny rekord pochodny.
/// Rekordy to też naturalny model danych dla przyszłych edytorów (property grid).
/// Nowy typ stanu trzeba dopisać do listy JsonDerivedType poniżej.
/// Tablice w rekordach są głębokimi kopiami zrobionymi przy CaptureState i traktuje się je jako tylko do odczytu
/// (RestoreState kopiuje je z powrotem, więc snapshot nigdy nie dzieli pamięci z żywym modułem).
/// Wygenerowane Equals rekordu porównuje tablice po referencji — do porównania treści służy <see cref="SameAs"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ApproachTargetState), "approachTarget")]
[JsonDerivedType(typeof(NeuralNetworkState), "neuralNetwork")]
[JsonDerivedType(typeof(ConstantState), "constant")]
[JsonDerivedType(typeof(AvoidAndSeekState), "avoidAndSeek")]
[JsonDerivedType(typeof(CompositeState), "composite")]
[JsonDerivedType(typeof(CpgState), "cpg")]
public abstract record ModuleState
{
    public string ToJson() => JsonSerializer.Serialize(this);

    public static ModuleState FromJson(string aJson) =>
        JsonSerializer.Deserialize<ModuleState>(aJson) ?? throw new JsonException("Module state is null.");

    /// <summary>Czy stan ma tę samą treść (także zawartość tablic). Porównuje zapis JSON — floaty są w nim dokładne.</summary>
    public bool SameAs(ModuleState? aOther) =>
        aOther is not null && (ReferenceEquals(this, aOther) || (GetType() == aOther.GetType() && ToJson() == aOther.ToJson()));
}

public sealed record ApproachTargetState(float TurnGain, float StopGap, float SlowdownGap) : ModuleState;

public sealed record ConstantState(float Value) : ModuleState;

public sealed record AvoidAndSeekState(
    float[] RayAngles,
    float SteerGain,
    float AvoidGain,
    float PathWidth,
    float ClearProximity,
    float SideCommitment,
    float FrontAngle,
    float ReverseProximity,
    float ReverseDuration,
    float ReleaseProximity,
    float SideMemory,
    float StopGap,
    float SlowdownGap,
    float MinThrottle,
    float TurnAroundGap,
    float TurnAroundAngle) : ModuleState;

/// <summary>Pełny stan sieci: kształt, wagi, biasy oraz powiązania portów (głęboka kopia).</summary>
public sealed record NeuralNetworkState(
    int[] Layers,
    float[][][] Weights,
    float[][] Biases,
    string[] Ports,
    string[] InputExpressions,
    NeuralOutput[] Outputs) : ModuleState
{
    /// <summary>Liczba uczonych parametrów (wagi + biasy).</summary>
    [JsonIgnore]
    public int ParameterCount => Weights.Sum(aLayer => aLayer.Sum(aNeuron => aNeuron.Length)) + Biases.Sum(aLayer => aLayer.Length);
}

/// <summary>Stan podgrafu: stany modułów wnętrza (po Id), rekurencyjnie. Struktury grafu nie zapisuje.</summary>
public sealed record CompositeState(ModuleSnapshot[] Modules) : ModuleState;

/// <summary>Stan CPG węża: liczba stawów (kształt, należy do ciała) i 6 uczonych parametrów.</summary>
public sealed record CpgState(
    int Joints,
    float Amplitude,
    float Frequency,
    float PhaseLag,
    float TurnGain,
    float PitchAmplitude,
    float PitchPhase) : ModuleState;

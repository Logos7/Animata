using System.Text.Json;
using System.Text.Json.Serialization;
using Animata.Core.Brains.Modules;
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
[JsonDerivedType(typeof(RouterState), "router")]
[JsonDerivedType(typeof(AvoidAndSeekState), "avoidAndSeek")]
[JsonDerivedType(typeof(CompositeState), "composite")]
[JsonDerivedType(typeof(CpgState), "cpg")]
[JsonDerivedType(typeof(GaitState), "gait")]
[JsonDerivedType(typeof(StateMachineState), "stateMachine")]
[JsonDerivedType(typeof(BalanceState), "balance")]
[JsonDerivedType(typeof(BipedGaitState), "bipedGait")]
public abstract record ModuleState
{
    public string ToJson() => JsonSerializer.Serialize(this);

    public static ModuleState FromJson(string aJson) =>
        JsonSerializer.Deserialize<ModuleState>(aJson) ?? throw new JsonException("Module state is null.");

    /// <summary>Czy stan ma tę samą treść (także zawartość tablic). Porównuje zapis JSON — floaty są w nim dokładne.</summary>
    public bool SameAs(ModuleState? aOther) =>
        aOther is not null && (ReferenceEquals(this, aOther) || (GetType() == aOther.GetType() && ToJson() == aOther.ToJson()));

    /// <summary>
    /// Nowy moduł o kształcie tego stanu (z podanym Id), gotowy na <see cref="BrainModule.RestoreState"/> — dzięki temu
    /// zapis świata i mózgu odtwarza każdy moduł opisany stanem bez listy typów. Null — stan nie opisuje całego modułu
    /// (podgraf: jego wnętrze to struktura, zapisuje się osobno). Nowy typ modułu: rekord stanu z tą metodą i wpis JsonDerivedType wyżej.
    /// </summary>
    public virtual BrainModule? CreateModule(Guid aId) => null;
}

public sealed record ApproachTargetState(float TurnGain, float StopGap, float SlowdownGap) : ModuleState
{
    public override BrainModule CreateModule(Guid aId) => new ApproachTargetModule { Id = aId };
}

/// <summary>Stała: wartość i port (null — stan sprzed zapisu portu; pasuje do każdej stałej).</summary>
public sealed record ConstantState(float Value, string? Port = null) : ModuleState
{
    public override BrainModule CreateModule(Guid aId) => new ConstantModule(Port ?? "Value", Value) { Id = aId };
}

/// <summary>Router: liczba kanałów i porty kanału — sama konfiguracja (router nie ma parametrów).</summary>
public sealed record RouterState(int Channels, string[] Ports) : ModuleState
{
    public override BrainModule CreateModule(Guid aId) => new RouterModule(Channels, Ports) { Id = aId };
}

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
    float TurnAroundAngle) : ModuleState
{
    public override BrainModule CreateModule(Guid aId) => new AvoidAndSeekModule(RayAngles) { Id = aId };
}

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

    public override BrainModule CreateModule(Guid aId) => new NeuralNetworkModule(new NeuralNetwork([.. Layers])) { Id = aId };
}

/// <summary>Stan podgrafu: stany modułów wnętrza (po Id), rekurencyjnie. Struktury grafu nie zapisuje.</summary>
public sealed record CompositeState(ModuleSnapshot[] Modules) : ModuleState;

/// <summary>Stan generatora chodu czworonoga: 6 uczonych parametrów.</summary>
public sealed record GaitState(float Stride, float Lift, float Knee, float KneeSwing, float Frequency, float TurnGain) : ModuleState
{
    public override BrainModule CreateModule(Guid aId) => new GaitModule { Id = aId };
}

/// <summary>Stan CPG węża: liczba stawów (kształt, należy do ciała) i 6 uczonych parametrów.</summary>
public sealed record CpgState(
    int Joints,
    float Amplitude,
    float Frequency,
    float PhaseLag,
    float TurnGain,
    float PitchAmplitude,
    float PitchPhase,
    bool Grip = false) : ModuleState
{
    public override BrainModule CreateModule(Guid aId) => new CpgModule(Joints) { Id = aId };
}

/// <summary>Automat stanów: stany, porty warunków, porty wyjść, przejścia, czas przejścia i najkrótszy pobyt (sama konfiguracja).</summary>
public sealed record StateMachineState(
    string[] States,
    string[] Conditions,
    string[] Ports,
    StateTransition[] Transitions,
    float BlendSeconds,
    float MinDwellSeconds) : ModuleState
{
    public override BrainModule CreateModule(Guid aId) => new StateMachineModule(States, Conditions, Ports, Transitions) { Id = aId };
}

/// <summary>Ręczne stanie humanoida: 6 wzmocnień regulatora z błędnika.</summary>
public sealed record BalanceState(float AnkleP, float AnkleD, float HipP, float HipD, float RollP, float RollD) : ModuleState
{
    public override BrainModule CreateModule(Guid aId) => new BalanceModule { Id = aId };
}

/// <summary>Ręczny chód humanoida: 9 uczonych parametrów generatora kroku.</summary>
public sealed record BipedGaitState(
    float Stride, float KneeLift, float KneePhase, float Sway, float Frequency, float Lean, float BalanceP, float BalanceD, float TurnGain) : ModuleState
{
    public override BrainModule CreateModule(Guid aId) => new BipedGaitModule { Id = aId };
}

using Animata.Core.Actuators;
using Animata.Core.Sensors;

namespace Animata.Core.Brains.Modules;

/// <summary>
/// Generator wzorca ruchu (CPG) węża: fala biegnąca od głowy do ogona.
/// Staw k: Yaw{k} = napęd · A · sin(φ − k·λ) + skręt, Pitch{k} = P · sin(φ − k·λ + ψ), φ rośnie o 2π·f na sekundę.
/// Skręt = −TurnGain · (kąt do celu / π) — wygina całe ciało w łuk tak, że dodatni TurnGain prowadzi głowę do celu
/// (skręt stawu w lewo zawraca ogon w lewo, a głowę w prawo — stąd minus; sprawdzone pomiarem). Napęd słabnie przy samym celu
/// (szczelina &lt; 0.6 m), żeby wąż się przy nim zatrzymał. Wejścia: Found, Gap, DirectionX, DirectionY (oko);
/// wyjścia jak porty <see cref="SpineActuator"/>.
/// Uczone parametry (6, niezależnie od liczby segmentów — wyuczony chód przeżywa zmianę długości węża):
/// amplituda A, częstotliwość f, przesunięcie fazy λ między stawami, TurnGain, amplituda pochylenia P, faza pochylenia ψ.
/// Stan chwilowy: faza φ (czyści ją <see cref="Reset"/>).
/// </summary>
public sealed class CpgModule : BrainModule, ITrainableModule
{
    public const int Parameters = 6;

    /// <summary>Szczelina do celu, poniżej której napęd maleje do zera.</summary>
    public const float ArrivalGap = 0.6f;

    private static readonly string[] Inputs =
    [
        TargetSensor.FoundPort, TargetSensor.GapPort, TargetSensor.DirectionXPort, TargetSensor.DirectionYPort
    ];

    private readonly Dictionary<string, float> _outputs = [];
    private string[] _ports = [];
    private float _phase;

    public CpgModule(int aJoints) => SetJointCount(aJoints);

    public int Joints { get; private set; }

    public float Amplitude { get; set; } = 0.35f;
    public float Frequency { get; set; } = 1.2f;
    public float PhaseLag { get; set; } = 0.9f;
    public float TurnGain { get; set; } = 0.5f;
    public float PitchAmplitude { get; set; }
    public float PitchPhase { get; set; } = MathF.PI / 2;

    /// <summary>
    /// Chwyt (wspinacz): przy celu fala zwalnia do zera, ale zgięcie zostaje — zwój dalej ściska pień. Bez chwytu
    /// (pełzanie) przy celu maleje amplituda i wąż się prostuje. Ustawienie, nie uczony parametr.
    /// </summary>
    public bool Grip { get; set; }

    public override IReadOnlyList<string> InputPorts => Inputs;
    public override IReadOnlyList<string> OutputPorts => _ports;

    public int ParameterCount => Parameters;

    public void SetJointCount(int aJoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(aJoints);
        Joints = aJoints;
        _ports = SpineActuator.PortsFor(aJoints);
        _outputs.Clear();
        foreach (var port in _ports)
            _outputs[port] = 0;
    }

    public override IReadOnlyDictionary<string, float> Evaluate(
        IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        var found = aInputs.GetValueOrDefault(TargetSensor.FoundPort) > 0;
        var bearing = found
            ? MathF.Atan2(aInputs.GetValueOrDefault(TargetSensor.DirectionYPort), aInputs.GetValueOrDefault(TargetSensor.DirectionXPort)) / MathF.PI
            : 0;
        var drive = found ? Math.Clamp(aInputs.GetValueOrDefault(TargetSensor.GapPort) / ArrivalGap, 0, 1) : 1;
        _phase = (_phase + MathF.Tau * Math.Clamp(Frequency, 0, 3) * (Grip ? drive : 1) * aContext.Delta) % MathF.Tau;
        var shape = Grip ? 1 : drive;
        var turn = Math.Clamp(-TurnGain * bearing, -1, 1) * drive;
        var amplitude = Math.Clamp(Amplitude, 0, 1) * shape;
        var pitch = Math.Clamp(PitchAmplitude, 0, 1) * shape;

        for (var joint = 0; joint < Joints; joint++)
        {
            var wave = _phase - joint * PhaseLag;
            _outputs[SpineActuator.YawPort(joint)] = Math.Clamp(amplitude * MathF.Sin(wave) + turn, -1, 1);
            _outputs[SpineActuator.PitchPort(joint)] = Math.Clamp(pitch * MathF.Sin(wave + PitchPhase), -1, 1);
        }
        return _outputs;
    }

    public override void Reset() => _phase = 0;

    public float[] GetParameters() => [Amplitude, Frequency, PhaseLag, TurnGain, PitchAmplitude, PitchPhase];

    /// <summary>Wpisuje parametry, przycinając je do sensownych zakresów (ewolucja operuje na surowych liczbach).</summary>
    public void SetParameters(ReadOnlySpan<float> aParameters)
    {
        if (aParameters.Length != Parameters)
            throw new ArgumentException($"Expected {Parameters} parameters, got {aParameters.Length}.", nameof(aParameters));
        Amplitude = Clean(aParameters[0], 0, 1);
        Frequency = Clean(aParameters[1], 0, 3);
        PhaseLag = Clean(aParameters[2], -MathF.PI, MathF.PI);
        TurnGain = Clean(aParameters[3], -3, 3);
        PitchAmplitude = Clean(aParameters[4], 0, 1);
        PitchPhase = Clean(aParameters[5], -MathF.PI, MathF.PI);
    }

    public void Randomize(Random? aRandom = null)
    {
        var random = aRandom ?? Random.Shared;
        float Between(float aMin, float aMax) => aMin + random.NextSingle() * (aMax - aMin);
        SetParameters([Between(0, 1), Between(0.2f, 2), Between(-MathF.PI, MathF.PI), Between(-1.5f, 1.5f), Between(0, 0.5f),
            Between(-MathF.PI, MathF.PI)]);
    }

    public override ModuleState CaptureState() =>
        new CpgState(Joints, Amplitude, Frequency, PhaseLag, TurnGain, PitchAmplitude, PitchPhase, Grip);

    /// <summary>Przywraca parametry. Liczba stawów należy do ciała, więc stan z inną liczbą stawów też pasuje.</summary>
    public override void RestoreState(ModuleState aState)
    {
        if (aState is not CpgState state)
            throw new ArgumentException($"Expected {nameof(CpgState)}, got {aState.GetType().Name}.", nameof(aState));
        SetParameters([state.Amplitude, state.Frequency, state.PhaseLag, state.TurnGain, state.PitchAmplitude, state.PitchPhase]);
        Grip = state.Grip;
    }

    public static CpgModule Create(CpgState aShape, ReadOnlySpan<float> aParameters, string aName = "CPG")
    {
        var module = new CpgModule(aShape.Joints) { Name = aName, Grip = aShape.Grip };
        module.SetParameters(aParameters);
        return module;
    }

    private static float Clean(float aValue, float aMin, float aMax) => float.IsFinite(aValue) ? Math.Clamp(aValue, aMin, aMax) : aMin;
}

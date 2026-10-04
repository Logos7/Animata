using Animata.Core.Actuators;
using Animata.Core.Bodies;
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
public sealed class CpgModule : ParametricModule
{
    /// <summary>Szczelina do celu, poniżej której napęd maleje do zera.</summary>
    public const float ArrivalGap = 0.6f;

    public static readonly ParameterSpec[] Specs =
    [
        new("Amplitude", 0, 1, 0, 1),
        new("Frequency", 0, 3, 0.2f, 2),
        new("PhaseLag", -MathF.PI, MathF.PI, -MathF.PI, MathF.PI),
        new("TurnGain", -3, 3, -1.5f, 1.5f),
        new("PitchAmplitude", 0, 1, 0, 0.5f),
        new("PitchPhase", -MathF.PI, MathF.PI, -MathF.PI, MathF.PI)
    ];

    private readonly Dictionary<string, float> _outputs = [];
    private string[] _ports = [];
    private float _phase;

    public CpgModule(int aJoints) : base(Specs, [0.35f, 1.2f, 0.9f, 0.5f, 0, MathF.PI / 2]) => SetJointCount(aJoints);

    public int Joints { get; private set; }

    public float Amplitude { get => Values[0]; set => Values[0] = value; }
    public float Frequency { get => Values[1]; set => Values[1] = value; }
    public float PhaseLag { get => Values[2]; set => Values[2] = value; }
    public float TurnGain { get => Values[3]; set => Values[3] = value; }
    public float PitchAmplitude { get => Values[4]; set => Values[4] = value; }
    public float PitchPhase { get => Values[5]; set => Values[5] = value; }

    /// <summary>
    /// Chwyt (wspinacz): przy celu fala zwalnia do zera, ale zgięcie zostaje — zwój dalej ściska pień. Bez chwytu
    /// (pełzanie) przy celu maleje amplituda i wąż się prostuje. Ustawienie, nie uczony parametr.
    /// </summary>
    public bool Grip { get; set; }

    public override IReadOnlyList<string> InputPorts => TargetSensor.SteeringPorts;
    public override IReadOnlyList<string> OutputPorts => _ports;

    public void SetJointCount(int aJoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(aJoints);
        Joints = aJoints;
        _ports = JointPorts.For(aJoints);
        _outputs.Clear();
        foreach (var port in _ports)
            _outputs[port] = 0;
    }

    public override IReadOnlyDictionary<string, float> Evaluate(
        IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        var course = TargetSensor.Course(aInputs);
        var drive = course.Drive(ArrivalGap);
        _phase = (_phase + MathF.Tau * Math.Clamp(Frequency, 0, 3) * (Grip ? drive : 1) * aContext.Delta) % MathF.Tau;
        var shape = Grip ? 1 : drive;
        var turn = Math.Clamp(-TurnGain * course.Bearing, -1, 1) * drive;
        var amplitude = Math.Clamp(Amplitude, 0, 1) * shape;
        var pitch = Math.Clamp(PitchAmplitude, 0, 1) * shape;

        for (var joint = 0; joint < Joints; joint++)
        {
            var wave = _phase - joint * PhaseLag;
            _outputs[JointPorts.Yaw(joint)] = Math.Clamp(amplitude * MathF.Sin(wave) + turn, -1, 1);
            _outputs[JointPorts.Pitch(joint)] = Math.Clamp(pitch * MathF.Sin(wave + PitchPhase), -1, 1);
        }
        return _outputs;
    }

    public override Func<bool> CaptureConfigurationCheck()
    {
        var joints = Joints;
        var grip = Grip;
        return () => Joints == joints && Grip == grip;
    }

    public override void Reset() => _phase = 0;

    public override ModuleState CaptureState() =>
        new CpgState(Joints, Amplitude, Frequency, PhaseLag, TurnGain, PitchAmplitude, PitchPhase, Grip);

    /// <summary>Przywraca parametry. Liczba stawów należy do ciała, więc stan z inną liczbą stawów też pasuje.</summary>
    public override void RestoreState(ModuleState aState)
    {
        var state = Expect<CpgState>(aState);
        SetParameters([state.Amplitude, state.Frequency, state.PhaseLag, state.TurnGain, state.PitchAmplitude, state.PitchPhase]);
        Grip = state.Grip;
    }
}

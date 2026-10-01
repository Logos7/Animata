using Animata.Core.Actuators;
using Animata.Core.Bodies;
using Animata.Core.Sensors;
using Animata.Core.WorldObjects;

namespace Animata.Core.Brains.Modules;

/// <summary>
/// Ręczne stanie humanoida (punkt odniesienia dla sieci): regulator PD z błędnika (<see cref="BalanceSensor"/>) na kostki
/// i biodra — strategia kostki (stopa przesuwa nacisk pod ciałem) i biodra (tułów przeciwdziała pochyleniu). Wszystkie
/// pozostałe stawy trzymają pozę spoczynkową. 6 uczonych parametrów: wzmocnienia P i D pochylenia dla kostek i bioder,
/// P i D przechyłu dla kostek i odwiedzenia bioder. Znaki wzmocnień dobiera pomiar (albo nauka) — zero to postawa sztywna.
/// </summary>
public sealed class BalanceModule() : ParametricModule(Specs, new float[6])
{
    /// <summary>Wzmocnienia regulatora: przycinane do ±5 (NaN — 0), losowane z ±2.</summary>
    public static readonly ParameterSpec[] Specs = ParameterSpec.Same(-5, 5, -2, 2, 0, "AnkleP", "AnkleD", "HipP", "HipD", "RollP", "RollD");

    private static readonly string[] Inputs = [BalanceSensor.PitchPort, BalanceSensor.RollPort, BalanceSensor.PitchRatePort, BalanceSensor.RollRatePort];
    private readonly Dictionary<string, float> _outputs = Humanoid.Ports.ToDictionary(aPort => aPort, _ => 0f);

    public float AnkleP { get => Values[0]; set => Values[0] = value; }
    public float AnkleD { get => Values[1]; set => Values[1] = value; }
    public float HipP { get => Values[2]; set => Values[2] = value; }
    public float HipD { get => Values[3]; set => Values[3] = value; }
    public float RollP { get => Values[4]; set => Values[4] = value; }
    public float RollD { get => Values[5]; set => Values[5] = value; }

    public override IReadOnlyList<string> InputPorts => Inputs;
    public override IReadOnlyList<string> OutputPorts => Humanoid.Ports;

    public override IReadOnlyDictionary<string, float> Evaluate(IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        var pitch = aInputs.GetValueOrDefault(BalanceSensor.PitchPort);
        var roll = aInputs.GetValueOrDefault(BalanceSensor.RollPort);
        var pitchRate = aInputs.GetValueOrDefault(BalanceSensor.PitchRatePort);
        var rollRate = aInputs.GetValueOrDefault(BalanceSensor.RollRatePort);
        var ankle = Math.Clamp(AnkleP * pitch + AnkleD * pitchRate, -1, 1);
        var hip = Math.Clamp(HipP * pitch + HipD * pitchRate, -1, 1);
        var side = Math.Clamp(RollP * roll + RollD * rollRate, -1, 1);
        for (var leg = 0; leg < 2; leg++)
        {
            _outputs[JointPorts.Pitch(Humanoid.Ankle(leg))] = ankle;
            _outputs[JointPorts.Pitch(Humanoid.Hip(leg))] = hip;
            _outputs[JointPorts.Yaw(Humanoid.Ankle(leg))] = side;
            _outputs[JointPorts.Yaw(Humanoid.Hip(leg))] = side;
        }
        return _outputs;
    }

    public override ModuleState CaptureState() => new BalanceState(AnkleP, AnkleD, HipP, HipD, RollP, RollD);

    public override void RestoreState(ModuleState aState)
    {
        var state = Expect<BalanceState>(aState);
        SetParameters([state.AnkleP, state.AnkleD, state.HipP, state.HipD, state.RollP, state.RollD]);
    }
}

/// <summary>
/// Ręczny chód humanoida (generator kroku): nogi w przeciwfazie; faza φ rośnie o 2π·f na sekundę.
/// - zamach biodra: lewa −krok·sin φ, prawa +krok·sin φ (ujemne pochylenie biodra = noga do przodu), krok = A · napęd ·
///   (1 ± skręt) — przy skręcie jedna noga stawia dłuższe kroki;
/// - noga przenoszona (sin φ &gt; 0 dla lewej) zgina kolano: K · max(0, sin(φ + ψ)), kostka równoważy: −0.5 · kolano;
/// - kołysanie na boki (przeniesienie ciężaru nad nogę podporową): odwiedzenie bioder i przechył kostek S · cos φ;
/// - pochylenie tułowia do przodu (L) i regulator równowagi z błędnika na biodra: P·Pitch + D·PitchRate;
/// - skręt = TurnGain · (kąt do celu / π); napęd słabnie przy celu.
/// 9 uczonych parametrów: A, K, ψ, S, f, L, P, D, TurnGain. Faza — stan chwilowy (Reset).
/// </summary>
public sealed class BipedGaitModule() : ParametricModule(Specs, [0.26f, 0, -0.87f, -0.36f, 1.7f, 0.19f, -0.19f, 0.03f, -0.17f])
{
    public static readonly ParameterSpec[] Specs =
    [
        new("Stride", 0, 1, 0, 0.6f),
        new("KneeLift", 0, 1, 0, 0.8f),
        new("KneePhase", -MathF.PI, MathF.PI, -3, 3),
        new("Sway", -1, 1, -0.4f, 0.4f),
        new("Frequency", 0, 3, 0.4f, 2.5f),
        new("Lean", -1, 1, -0.3f, 0.3f),
        new("BalanceP", -5, 5, -2, 2),
        new("BalanceD", -5, 5, -2, 2),
        new("TurnGain", -3, 3, -1.5f, 1.5f)
    ];

    /// <summary>Szczelina do celu, poniżej której napęd maleje do zera.</summary>
    public const float ArrivalGap = 0.5f;

    private static readonly string[] Inputs =
        [.. TargetSensor.SteeringPorts, BalanceSensor.PitchPort, BalanceSensor.PitchRatePort];

    private readonly Dictionary<string, float> _outputs = Humanoid.Ports.ToDictionary(aPort => aPort, _ => 0f);
    private float _phase;

    // Domyślne — z ewolucji na próbach chodu (15 + 15 pokoleń): drobi bez podnoszenia kolan, kołysząc się na boki;
    // nie upada w żadnej z 8 prób i dochodzi do celu w 7/8. Ręczne startowe (krok 0.25, kolano 0.35) przewracały go zawsze.
    public float Stride { get => Values[0]; set => Values[0] = value; }
    public float KneeLift { get => Values[1]; set => Values[1] = value; }
    public float KneePhase { get => Values[2]; set => Values[2] = value; }
    public float Sway { get => Values[3]; set => Values[3] = value; }
    public float Frequency { get => Values[4]; set => Values[4] = value; }
    public float Lean { get => Values[5]; set => Values[5] = value; }
    public float BalanceP { get => Values[6]; set => Values[6] = value; }
    public float BalanceD { get => Values[7]; set => Values[7] = value; }
    public float TurnGain { get => Values[8]; set => Values[8] = value; }

    public override IReadOnlyList<string> InputPorts => Inputs;
    public override IReadOnlyList<string> OutputPorts => Humanoid.Ports;

    public override IReadOnlyDictionary<string, float> Evaluate(IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        _phase = (_phase + MathF.Tau * Math.Clamp(Frequency, 0, 3) * aContext.Delta) % MathF.Tau;
        var course = TargetSensor.Course(aInputs);
        var drive = course.Drive(ArrivalGap);
        var turn = Math.Clamp(TurnGain * course.Bearing, -1, 1);
        var balance = BalanceP * aInputs.GetValueOrDefault(BalanceSensor.PitchPort) + BalanceD * aInputs.GetValueOrDefault(BalanceSensor.PitchRatePort);

        for (var leg = 0; leg < 2; leg++)
        {
            var side = leg == 0 ? 1f : -1f;   // lewa, prawa
            var phase = _phase + (leg == 0 ? 0 : MathF.PI);
            var stride = Stride * drive * (1 - turn * side);
            var hip = -stride * MathF.Sin(phase) + Lean + balance;
            var knee = KneeLift * drive * MathF.Max(0, MathF.Sin(phase + KneePhase));
            _outputs[JointPorts.Pitch(Humanoid.Hip(leg))] = Math.Clamp(hip, -1, 1);
            _outputs[JointPorts.Pitch(Humanoid.Knee(leg))] = Math.Clamp(knee, 0, 1);
            _outputs[JointPorts.Pitch(Humanoid.Ankle(leg))] = Math.Clamp(-0.5f * knee - hip * 0.5f, -1, 1);
            var sway = Sway * drive * MathF.Cos(_phase);
            _outputs[JointPorts.Yaw(Humanoid.Hip(leg))] = Math.Clamp(sway, -1, 1);
            _outputs[JointPorts.Yaw(Humanoid.Ankle(leg))] = Math.Clamp(-sway, -1, 1);
        }
        return _outputs;
    }

    public override void Reset() => _phase = 0;

    public override ModuleState CaptureState() =>
        new BipedGaitState(Stride, KneeLift, KneePhase, Sway, Frequency, Lean, BalanceP, BalanceD, TurnGain);

    public override void RestoreState(ModuleState aState)
    {
        var state = Expect<BipedGaitState>(aState);
        SetParameters([state.Stride, state.KneeLift, state.KneePhase, state.Sway, state.Frequency, state.Lean,
            state.BalanceP, state.BalanceD, state.TurnGain]);
    }
}

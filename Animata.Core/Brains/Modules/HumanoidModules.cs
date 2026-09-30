using Animata.Core.Actuators;
using Animata.Core.Sensors;
using Animata.Core.WorldObjects;

namespace Animata.Core.Brains.Modules;

/// <summary>
/// Ręczne stanie humanoida (punkt odniesienia dla sieci): regulator PD z błędnika (<see cref="BalanceSensor"/>) na kostki
/// i biodra — strategia kostki (stopa przesuwa nacisk pod ciałem) i biodra (tułów przeciwdziała pochyleniu). Wszystkie
/// pozostałe stawy trzymają pozę spoczynkową. 6 uczonych parametrów: wzmocnienia P i D pochylenia dla kostek i bioder,
/// P i D przechyłu dla kostek i odwiedzenia bioder. Znaki wzmocnień dobiera pomiar (albo nauka) — zero to postawa sztywna.
/// </summary>
public sealed class BalanceModule : BrainModule, ITrainableModule
{
    public const int Parameters = 6;

    private static readonly string[] Inputs = [BalanceSensor.PitchPort, BalanceSensor.RollPort, BalanceSensor.PitchRatePort, BalanceSensor.RollRatePort];
    private readonly Dictionary<string, float> _outputs = Humanoid.Ports.ToDictionary(aPort => aPort, _ => 0f);

    public float AnkleP { get; set; }
    public float AnkleD { get; set; }
    public float HipP { get; set; }
    public float HipD { get; set; }
    public float RollP { get; set; }
    public float RollD { get; set; }

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
            _outputs[SpineActuator.PitchPort(Humanoid.Ankle(leg))] = ankle;
            _outputs[SpineActuator.PitchPort(Humanoid.Hip(leg))] = hip;
            _outputs[SpineActuator.YawPort(Humanoid.Ankle(leg))] = side;
            _outputs[SpineActuator.YawPort(Humanoid.Hip(leg))] = side;
        }
        return _outputs;
    }

    public float[] GetParameters() => [AnkleP, AnkleD, HipP, HipD, RollP, RollD];

    public void SetParameters(ReadOnlySpan<float> aParameters)
    {
        if (aParameters.Length != Parameters)
            throw new ArgumentException($"Expected {Parameters} parameters, got {aParameters.Length}.", nameof(aParameters));
        AnkleP = Clean(aParameters[0]);
        AnkleD = Clean(aParameters[1]);
        HipP = Clean(aParameters[2]);
        HipD = Clean(aParameters[3]);
        RollP = Clean(aParameters[4]);
        RollD = Clean(aParameters[5]);
    }

    public void Randomize(Random? aRandom = null)
    {
        var random = aRandom ?? Random.Shared;
        SetParameters([.. Enumerable.Range(0, Parameters).Select(_ => (random.NextSingle() * 2 - 1) * 2)]);
    }

    public override ModuleState CaptureState() => new BalanceState(AnkleP, AnkleD, HipP, HipD, RollP, RollD);

    public override void RestoreState(ModuleState aState)
    {
        var state = Expect<BalanceState>(aState);
        SetParameters([state.AnkleP, state.AnkleD, state.HipP, state.HipD, state.RollP, state.RollD]);
    }

    public static BalanceModule Create(ReadOnlySpan<float> aParameters, string aName = "Stanie")
    {
        var module = new BalanceModule { Name = aName };
        module.SetParameters(aParameters);
        return module;
    }

    private static float Clean(float aValue) => float.IsFinite(aValue) ? Math.Clamp(aValue, -5, 5) : 0;
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
public sealed class BipedGaitModule : BrainModule, ITrainableModule
{
    public const int Parameters = 9;

    /// <summary>Szczelina do celu, poniżej której napęd maleje do zera.</summary>
    public const float ArrivalGap = 0.5f;

    private static readonly string[] Inputs =
        [.. TargetSensor.SteeringPorts, BalanceSensor.PitchPort, BalanceSensor.PitchRatePort];

    private readonly Dictionary<string, float> _outputs = Humanoid.Ports.ToDictionary(aPort => aPort, _ => 0f);
    private float _phase;

    // Domyślne — z ewolucji na próbach chodu (15 + 15 pokoleń): drobi bez podnoszenia kolan, kołysząc się na boki;
    // nie upada w żadnej z 8 prób i dochodzi do celu w 7/8. Ręczne startowe (krok 0.25, kolano 0.35) przewracały go zawsze.
    public float Stride { get; set; } = 0.26f;
    public float KneeLift { get; set; }
    public float KneePhase { get; set; } = -0.87f;
    public float Sway { get; set; } = -0.36f;
    public float Frequency { get; set; } = 1.7f;
    public float Lean { get; set; } = 0.19f;
    public float BalanceP { get; set; } = -0.19f;
    public float BalanceD { get; set; } = 0.03f;
    public float TurnGain { get; set; } = -0.17f;

    public override IReadOnlyList<string> InputPorts => Inputs;
    public override IReadOnlyList<string> OutputPorts => Humanoid.Ports;

    public override IReadOnlyDictionary<string, float> Evaluate(IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        _phase = (_phase + MathF.Tau * Math.Clamp(Frequency, 0, 3) * aContext.Delta) % MathF.Tau;
        var found = aInputs.GetValueOrDefault(TargetSensor.FoundPort) > 0;
        var bearing = found
            ? MathF.Atan2(aInputs.GetValueOrDefault(TargetSensor.DirectionYPort), aInputs.GetValueOrDefault(TargetSensor.DirectionXPort)) / MathF.PI
            : 0;
        var drive = found ? Math.Clamp(aInputs.GetValueOrDefault(TargetSensor.GapPort) / ArrivalGap, 0, 1) : 1;
        var turn = Math.Clamp(TurnGain * bearing, -1, 1);
        var balance = BalanceP * aInputs.GetValueOrDefault(BalanceSensor.PitchPort) + BalanceD * aInputs.GetValueOrDefault(BalanceSensor.PitchRatePort);

        for (var leg = 0; leg < 2; leg++)
        {
            var side = leg == 0 ? 1f : -1f;   // lewa, prawa
            var phase = _phase + (leg == 0 ? 0 : MathF.PI);
            var stride = Stride * drive * (1 - turn * side);
            var hip = -stride * MathF.Sin(phase) + Lean + balance;
            var knee = KneeLift * drive * MathF.Max(0, MathF.Sin(phase + KneePhase));
            _outputs[SpineActuator.PitchPort(Humanoid.Hip(leg))] = Math.Clamp(hip, -1, 1);
            _outputs[SpineActuator.PitchPort(Humanoid.Knee(leg))] = Math.Clamp(knee, 0, 1);
            _outputs[SpineActuator.PitchPort(Humanoid.Ankle(leg))] = Math.Clamp(-0.5f * knee - hip * 0.5f, -1, 1);
            var sway = Sway * drive * MathF.Cos(_phase);
            _outputs[SpineActuator.YawPort(Humanoid.Hip(leg))] = Math.Clamp(sway, -1, 1);
            _outputs[SpineActuator.YawPort(Humanoid.Ankle(leg))] = Math.Clamp(-sway, -1, 1);
        }
        return _outputs;
    }

    public override void Reset() => _phase = 0;

    public float[] GetParameters() => [Stride, KneeLift, KneePhase, Sway, Frequency, Lean, BalanceP, BalanceD, TurnGain];

    public void SetParameters(ReadOnlySpan<float> aParameters)
    {
        if (aParameters.Length != Parameters)
            throw new ArgumentException($"Expected {Parameters} parameters, got {aParameters.Length}.", nameof(aParameters));
        Stride = Clean(aParameters[0], 0, 1);
        KneeLift = Clean(aParameters[1], 0, 1);
        KneePhase = Clean(aParameters[2], -MathF.PI, MathF.PI);
        Sway = Clean(aParameters[3], -1, 1);
        Frequency = Clean(aParameters[4], 0, 3);
        Lean = Clean(aParameters[5], -1, 1);
        BalanceP = Clean(aParameters[6], -5, 5);
        BalanceD = Clean(aParameters[7], -5, 5);
        TurnGain = Clean(aParameters[8], -3, 3);
    }

    public void Randomize(Random? aRandom = null)
    {
        var random = aRandom ?? Random.Shared;
        float Between(float aMin, float aMax) => aMin + random.NextSingle() * (aMax - aMin);
        SetParameters([Between(0, 0.6f), Between(0, 0.8f), Between(-3, 3), Between(-0.4f, 0.4f), Between(0.4f, 2.5f),
            Between(-0.3f, 0.3f), Between(-2, 2), Between(-2, 2), Between(-1.5f, 1.5f)]);
    }

    public override ModuleState CaptureState() =>
        new BipedGaitState(Stride, KneeLift, KneePhase, Sway, Frequency, Lean, BalanceP, BalanceD, TurnGain);

    public override void RestoreState(ModuleState aState)
    {
        var state = Expect<BipedGaitState>(aState);
        SetParameters([state.Stride, state.KneeLift, state.KneePhase, state.Sway, state.Frequency, state.Lean,
            state.BalanceP, state.BalanceD, state.TurnGain]);
    }

    public static BipedGaitModule Create(ReadOnlySpan<float> aParameters, string aName = "Chód")
    {
        var module = new BipedGaitModule { Name = aName };
        module.SetParameters(aParameters);
        return module;
    }

    private static float Clean(float aValue, float aMin, float aMax) => float.IsFinite(aValue) ? Math.Clamp(aValue, aMin, aMax) : aMin;
}

using Animata.Core.Actuators;
using Animata.Core.Sensors;

namespace Animata.Core.Brains.Modules;

/// <summary>
/// Generator chodu (CPG) czworonoga: kłus — przekątne pary nóg (przednia lewa z tylną prawą i odwrotnie) w przeciwfazie.
/// Noga n ma staw biodra 2n (Yaw = zamach przód–tył wokół pionu, Pitch = uniesienie) i kolana 2n+1 (Pitch = zgięcie).
/// Faza nogi θ = φ + (0 albo π); φ rośnie o 2π·f na sekundę:
/// - zamach: Yaw = strona · krok · sin θ (strona +1 dla prawych nóg, −1 dla lewych — dodatni skręt biodra prawej nogi
///   niesie stopę do przodu, lewej do tyłu); krok = A · napęd · (1 + skręt · strona), więc przy skręcie w lewo prawe nogi
///   stawiają dłuższe kroki;
/// - noga przenoszona do przodu (cos θ &gt; 0) jest uniesiona: Pitch biodra = −L · max(0, cos θ), kolano = K + Kz · max(0, cos θ);
/// - skręt = TurnGain · (kąt do celu / π): cel po lewej → dłuższe kroki prawych nóg; napęd słabnie przy celu.
/// Uczone parametry (6): krok A, uniesienie L, zgięcie kolana K, dodatkowe zgięcie przy przenoszeniu Kz, częstotliwość f, TurnGain.
/// Stan chwilowy: faza φ (Reset).
/// </summary>
public sealed class GaitModule : BrainModule, ITrainableModule
{
    public const int Parameters = 6;
    public const int Legs = 4;
    public const int Joints = Legs * 2;

    /// <summary>Szczelina do celu, poniżej której napęd maleje do zera.</summary>
    public const float ArrivalGap = 0.4f;

    /// <summary>Strona nogi: 0 przednia lewa, 1 przednia prawa, 2 tylna lewa, 3 tylna prawa.</summary>
    public static readonly float[] Side = [-1, 1, -1, 1];

    /// <summary>Przesunięcie fazy nogi — kłus: przekątne razem.</summary>
    public static readonly float[] Offset = [0, MathF.PI, MathF.PI, 0];

    private static readonly string[] Inputs =
    [
        TargetSensor.FoundPort, TargetSensor.GapPort, TargetSensor.DirectionXPort, TargetSensor.DirectionYPort
    ];

    private static readonly string[] Outputs = SpineActuator.PortsFor(Joints);
    private readonly Dictionary<string, float> _outputs = Outputs.ToDictionary(aPort => aPort, _ => 0f);
    private float _phase;

    public float Stride { get; set; } = 0.6f;
    public float Lift { get; set; } = 0.6f;
    public float Knee { get; set; }
    public float KneeSwing { get; set; } = -0.4f;
    public float Frequency { get; set; } = 2.5f;
    public float TurnGain { get; set; } = 1.5f;

    public float Phase => _phase;

    public override IReadOnlyList<string> InputPorts => Inputs;
    public override IReadOnlyList<string> OutputPorts => Outputs;

    public int ParameterCount => Parameters;

    public override IReadOnlyDictionary<string, float> Evaluate(IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        _phase = (_phase + MathF.Tau * Math.Clamp(Frequency, 0, 4) * aContext.Delta) % MathF.Tau;

        var found = aInputs.GetValueOrDefault(TargetSensor.FoundPort) > 0;
        var bearing = found
            ? MathF.Atan2(aInputs.GetValueOrDefault(TargetSensor.DirectionYPort), aInputs.GetValueOrDefault(TargetSensor.DirectionXPort)) / MathF.PI
            : 0;
        var drive = found ? Math.Clamp(aInputs.GetValueOrDefault(TargetSensor.GapPort) / ArrivalGap, 0, 1) : 1;
        var turn = Math.Clamp(TurnGain * bearing, -1, 1);

        for (var leg = 0; leg < Legs; leg++)
        {
            var theta = _phase + Offset[leg];
            var swing = MathF.Max(0, MathF.Cos(theta)) * drive;
            var stride = Math.Clamp(Stride * drive * (1 + turn * Side[leg]), 0, 1);
            _outputs[SpineActuator.YawPort(2 * leg)] = Math.Clamp(Side[leg] * stride * MathF.Sin(theta), -1, 1);
            _outputs[SpineActuator.PitchPort(2 * leg)] = Math.Clamp(-Lift * swing, -1, 1);
            _outputs[SpineActuator.YawPort(2 * leg + 1)] = 0;
            _outputs[SpineActuator.PitchPort(2 * leg + 1)] = Math.Clamp(Knee + KneeSwing * swing, -1, 1);
        }
        return _outputs;
    }

    public override void Reset() => _phase = 0;

    public float[] GetParameters() => [Stride, Lift, Knee, KneeSwing, Frequency, TurnGain];

    public void SetParameters(ReadOnlySpan<float> aParameters)
    {
        if (aParameters.Length != Parameters)
            throw new ArgumentException($"Expected {Parameters} parameters, got {aParameters.Length}.", nameof(aParameters));
        Stride = Clean(aParameters[0], 0, 1);
        Lift = Clean(aParameters[1], 0, 1);
        Knee = Clean(aParameters[2], -1, 1);
        KneeSwing = Clean(aParameters[3], -1, 1);
        Frequency = Clean(aParameters[4], 0, 4);
        TurnGain = Clean(aParameters[5], -3, 3);
    }

    public void Randomize(Random? aRandom = null)
    {
        var random = aRandom ?? Random.Shared;
        float Between(float aMin, float aMax) => aMin + random.NextSingle() * (aMax - aMin);
        SetParameters([Between(0, 1), Between(0, 1), Between(-1, 1), Between(-1, 1), Between(0.3f, 3), Between(-1.5f, 1.5f)]);
    }

    public override ModuleState CaptureState() => new GaitState(Stride, Lift, Knee, KneeSwing, Frequency, TurnGain);

    public override void RestoreState(ModuleState aState)
    {
        if (aState is not GaitState state)
            throw new ArgumentException($"Expected {nameof(GaitState)}, got {aState.GetType().Name}.", nameof(aState));
        SetParameters([state.Stride, state.Lift, state.Knee, state.KneeSwing, state.Frequency, state.TurnGain]);
    }

    public static GaitModule Create(ReadOnlySpan<float> aParameters, string aName = "Chód")
    {
        var module = new GaitModule { Name = aName };
        module.SetParameters(aParameters);
        return module;
    }

    private static float Clean(float aValue, float aMin, float aMax) => float.IsFinite(aValue) ? Math.Clamp(aValue, aMin, aMax) : aMin;
}

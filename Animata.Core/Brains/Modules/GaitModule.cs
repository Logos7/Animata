using Animata.Core.Actuators;
using Animata.Core.Bodies;
using Animata.Core.Sensors;

namespace Animata.Core.Brains.Modules;

/// <summary>
/// Generator chodu (CPG) czworonoga z nogami z trzech członów (<see cref="WorldObjects.Spider"/>): kłus — przekątne pary nóg
/// (przednia lewa z tylną prawą i odwrotnie) w przeciwfazie. Noga n ma staw zamachu 3n (Yaw — przód–tył wokół pionu),
/// uniesienia 3n+1 (Pitch — ujemne unosi udo) i kolana 3n+2 (Pitch — dodatnie zgina, kolano jest jednokierunkowe).
/// Faza nogi θ = φ + (0 albo π); φ rośnie o 2π·f na sekundę:
/// - zamach: Yaw = strona · krok · sin θ (strona +1 dla prawych nóg, −1 dla lewych — dodatni skręt prawej nogi niesie
///   stopę do przodu, lewej do tyłu); krok = A · napęd · (1 + skręt · strona), więc przy skręcie w lewo prawe nogi
///   stawiają dłuższe kroki;
/// - noga przenoszona do przodu (cos θ &gt; 0) jest uniesiona i zgięta: uniesienie = −L · max(0, cos θ),
///   kolano = K + Kz · max(0, cos θ) (ujemne wartości kolano obcina — nie wygina się wstecz);
/// - skręt = TurnGain · (kąt do celu / π): cel po lewej → dłuższe kroki prawych nóg; napęd słabnie przy celu.
/// Uczone parametry (6): krok A, uniesienie L, zgięcie kolana K, dodatkowe zgięcie przy przenoszeniu Kz, częstotliwość f, TurnGain.
/// Stan chwilowy: faza φ (Reset).
/// </summary>
public sealed class GaitModule() : ParametricModule(Specs, [0.6f, 0.6f, 0, 0.5f, 2.5f, 1.5f])
{
    public const int Legs = 4;
    public const int Joints = Legs * 3;

    /// <summary>Szczelina do celu, poniżej której napęd maleje do zera.</summary>
    public const float ArrivalGap = 0.4f;

    public static readonly ParameterSpec[] Specs =
    [
        new("Stride", 0, 1, 0, 1),
        new("Lift", 0, 1, 0, 1),
        new("Knee", -1, 1, -1, 1),
        new("KneeSwing", -1, 1, -1, 1),
        new("Frequency", 0, 4, 0.3f, 3),
        new("TurnGain", -3, 3, -1.5f, 1.5f)
    ];

    /// <summary>Strona nogi: 0 przednia lewa, 1 przednia prawa, 2 tylna lewa, 3 tylna prawa.</summary>
    public static readonly float[] Side = [-1, 1, -1, 1];

    /// <summary>Przesunięcie fazy nogi — kłus: przekątne razem.</summary>
    public static readonly float[] Offset = [0, MathF.PI, MathF.PI, 0];

    /// <summary>Porty ruchomych osi nóg, w kolejności portów nóg pająka: zamachy, potem uniesienia i kolana.</summary>
    public static readonly string[] Outputs =
    [
        .. Enumerable.Range(0, Legs).Select(aLeg => JointPorts.Yaw(3 * aLeg)),
        .. Enumerable.Range(0, Legs).SelectMany(aLeg => new[] { JointPorts.Pitch(3 * aLeg + 1), JointPorts.Pitch(3 * aLeg + 2) })
    ];

    private readonly Dictionary<string, float> _outputs = Outputs.ToDictionary(aPort => aPort, _ => 0f);
    private float _phase;

    public float Stride { get => Values[0]; set => Values[0] = value; }
    public float Lift { get => Values[1]; set => Values[1] = value; }
    public float Knee { get => Values[2]; set => Values[2] = value; }
    public float KneeSwing { get => Values[3]; set => Values[3] = value; }
    public float Frequency { get => Values[4]; set => Values[4] = value; }
    public float TurnGain { get => Values[5]; set => Values[5] = value; }

    public override IReadOnlyList<string> InputPorts => TargetSensor.SteeringPorts;
    public override IReadOnlyList<string> OutputPorts => Outputs;

    public override IReadOnlyDictionary<string, float> Evaluate(IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        _phase = (_phase + MathF.Tau * Math.Clamp(Frequency, 0, 4) * aContext.Delta) % MathF.Tau;

        var course = TargetSensor.Course(aInputs);
        var drive = course.Drive(ArrivalGap);
        var turn = Math.Clamp(TurnGain * course.Bearing, -1, 1);

        for (var leg = 0; leg < Legs; leg++)
        {
            var theta = _phase + Offset[leg];
            var swing = MathF.Max(0, MathF.Cos(theta)) * drive;
            var stride = Math.Clamp(Stride * drive * (1 + turn * Side[leg]), 0, 1);
            _outputs[JointPorts.Yaw(3 * leg)] = Math.Clamp(Side[leg] * stride * MathF.Sin(theta), -1, 1);
            _outputs[JointPorts.Pitch(3 * leg + 1)] = Math.Clamp(-Lift * swing, -1, 1);
            _outputs[JointPorts.Pitch(3 * leg + 2)] = Math.Clamp(Knee + KneeSwing * swing, -1, 1);
        }
        return _outputs;
    }

    public override void Reset() => _phase = 0;

    public override ModuleState CaptureState() => new GaitState(Stride, Lift, Knee, KneeSwing, Frequency, TurnGain);

    public override void RestoreState(ModuleState aState)
    {
        var state = Expect<GaitState>(aState);
        SetParameters([state.Stride, state.Lift, state.Knee, state.KneeSwing, state.Frequency, state.TurnGain]);
    }
}

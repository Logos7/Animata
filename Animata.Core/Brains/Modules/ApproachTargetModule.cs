using Animata.Core.Actuators;
using Animata.Core.Sensors;

namespace Animata.Core.Brains.Modules;

/// <summary>
/// Heurystyka „jedź do celu” jako czysta funkcja.
/// Wejścia (nazwy jak porty <see cref="TargetSensor"/>): Found, Gap, DirectionX, DirectionY.
/// Wyjścia (nazwy jak porty <see cref="DiskDriveActuator"/>): Turn ∈ [-1, 1], Step ∈ [0, 1].
/// </summary>
public sealed class ApproachTargetModule : BrainModule
{
    private static readonly string[] Outputs = [DiskDriveActuator.TurnPort, DiskDriveActuator.StepPort];

    private readonly Dictionary<string, float> _command = new()
    {
        [DiskDriveActuator.TurnPort] = 0,
        [DiskDriveActuator.StepPort] = 0
    };

    /// <summary>Znormalizowany skręt na radian błędu kursu.</summary>
    public float TurnGain { get; set; } = 4;

    /// <summary>Szczelina (powierzchnia–powierzchnia), przy której stwór się zatrzymuje.</summary>
    public float StopGap { get; set; } = 0.05f;

    /// <summary>Szczelina, poniżej której stwór zaczyna zwalniać.</summary>
    public float SlowdownGap { get; set; } = 1;

    public override IReadOnlyList<string> InputPorts => TargetSensor.SteeringPorts;
    public override IReadOnlyList<string> OutputPorts => Outputs;

    public override ModuleState CaptureState() => new ApproachTargetState(TurnGain, StopGap, SlowdownGap);

    public override void RestoreState(ModuleState aState)
    {
        var state = Expect<ApproachTargetState>(aState);
        TurnGain = state.TurnGain;
        StopGap = state.StopGap;
        SlowdownGap = state.SlowdownGap;
    }

    public override IReadOnlyDictionary<string, float> Evaluate(
        IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        var turn = 0f;
        var step = 0f;

        if (aInputs.GetValueOrDefault(TargetSensor.FoundPort) > 0)
        {
            var angle = MathF.Atan2(
                aInputs.GetValueOrDefault(TargetSensor.DirectionYPort),
                aInputs.GetValueOrDefault(TargetSensor.DirectionXPort));
            var remaining = aInputs.GetValueOrDefault(TargetSensor.GapPort) - StopGap;

            turn = Math.Clamp(angle * TurnGain, -1, 1);
            if (remaining > 0)
            {
                var alignment = MathF.Max(0, MathF.Cos(angle));
                var slowdown = SlowdownGap > 0 ? Math.Clamp(remaining / SlowdownGap, 0, 1) : 1;
                step = alignment * slowdown;
            }
        }

        _command[DiskDriveActuator.TurnPort] = turn;
        _command[DiskDriveActuator.StepPort] = step;
        return _command;
    }
}

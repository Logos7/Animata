using Animata.Core.Entities;

namespace Animata.Core.Actuators;

/// <summary>
/// Napęd z kierownicą. Steer ∈ [-1, 1] — ułamek maksymalnego kąta skrętu kół (dodatni = w lewo).
/// Throttle ∈ [-1, 1] — do przodu ułamek MaxSpeed, do tyłu ułamek MaxReverseSpeed.
/// Zadaje kołom stwora z części skręt (koła skrętne) i prędkość obwodową z momentem <see cref="DriveTorque"/>
/// (koła napędzane) — jazdę, poślizg i zderzenia liczy fizyka.
/// </summary>
public sealed class SteeringDriveActuator : Actuator
{
    public const string SteerPort = "Steer";
    public const string ThrottlePort = "Throttle";

    private static readonly string[] Ports = [SteerPort, ThrottlePort];

    [Setting]
    public float MaxSpeed { get; set; } = 2.5f;

    [Setting]
    public float MaxReverseSpeed { get; set; } = 1;

    /// <summary>Maksymalny kąt skrętu kół w radianach.</summary>
    [Setting]
    public float MaxSteerAngle { get; set; } = 35 * MathF.PI / 180;

    /// <summary>Największy moment silnika jednego koła napędzanego (N·m) — przyspieszenie i hamowanie.</summary>
    [Setting]
    public float DriveTorque { get; set; } = 3;

    /// <summary>Kopiuje ustawienia (nie stan) z innego napędu — np. do ciała w ukrytej próbie nauki.</summary>
    public void CopySettingsFrom(SteeringDriveActuator aOther)
    {
        MaxSpeed = aOther.MaxSpeed;
        MaxReverseSpeed = aOther.MaxReverseSpeed;
        MaxSteerAngle = aOther.MaxSteerAngle;
        DriveTorque = aOther.DriveTorque;
    }

    /// <summary>Ostatni kąt kół (rad) — do rysowania.</summary>
    public float SteerAngle { get; private set; }

    public override IReadOnlyList<string> InputPorts => Ports;

    public override void Apply(Entity aOwner, IReadOnlyDictionary<string, float> aCommands, float aDelta)
    {
        if (aDelta <= 0)
            return;

        var steer = aCommands.GetValueOrDefault(SteerPort);
        var throttle = aCommands.GetValueOrDefault(ThrottlePort);
        if (!float.IsFinite(steer) || !float.IsFinite(throttle))
            throw new ArgumentOutOfRangeException(nameof(aCommands), "Steering commands must be finite.");

        SteerAngle = Math.Clamp(steer, -1, 1) * MathF.Max(0, MaxSteerAngle);
        throttle = Math.Clamp(throttle, -1, 1);
        var speed = throttle >= 0 ? throttle * MathF.Max(0, MaxSpeed) : throttle * MathF.Max(0, MaxReverseSpeed);

        if (aOwner is not ArticulatedCreature body)
            return;
        for (var joint = 0; joint < body.JointCount; joint++)
            body.SetWheelTarget(joint, SteerAngle, speed, DriveTorque);
    }
}

using Animata.Core.Entities;

namespace Animata.Core.Actuators;

/// <summary>
/// Napęd różnicowy stwora z części: koła po lewej i prawej dostają różne prędkości (skręt w miejscu).
/// Turn ∈ [-1, 1] — ułamek maksymalnej prędkości obrotu (dodatni = w lewo, CCW).
/// Step ∈ [0, 1] — ułamek maksymalnej prędkości jazdy do przodu.
/// </summary>
public sealed class DiskDriveActuator : Actuator
{
    public const string TurnPort = "Turn";
    public const string StepPort = "Step";

    private static readonly string[] Ports = [TurnPort, StepPort];

    [Setting]
    public float MaxSpeed { get; set; } = 2;

    [Setting]
    public float MaxTurnSpeed { get; set; } = 2.5f;

    /// <summary>Największy moment silnika jednego koła (N·m).</summary>
    [Setting]
    public float DriveTorque { get; set; } = 3;

    /// <summary>Kopiuje ustawienia (nie stan) z innego napędu.</summary>
    public void CopySettingsFrom(DiskDriveActuator aOther)
    {
        MaxSpeed = aOther.MaxSpeed;
        MaxTurnSpeed = aOther.MaxTurnSpeed;
        DriveTorque = aOther.DriveTorque;
    }

    public override IReadOnlyList<string> InputPorts => Ports;

    public override void Apply(Entity aOwner, IReadOnlyDictionary<string, float> aCommands, float aDelta)
    {
        if (aDelta <= 0)
            return;

        var turn = aCommands.GetValueOrDefault(TurnPort);
        var step = aCommands.GetValueOrDefault(StepPort);
        if (!float.IsFinite(turn) || !float.IsFinite(step))
            throw new ArgumentOutOfRangeException(nameof(aCommands), "Drive commands must be finite.");

        // Ciało z części: napęd różnicowy — koło po lewej (Y > 0) wolniej przy skręcie w lewo, po prawej szybciej.
        if (aOwner is not ArticulatedCreature body)
            return;
        var forward = Math.Clamp(step, 0, 1) * MathF.Max(0, MaxSpeed);
        var rate = Math.Clamp(turn, -1, 1) * MathF.Max(0, MaxTurnSpeed);
        for (var joint = 0; joint < body.JointCount; joint++)
            body.SetWheelTarget(joint, 0, forward - rate * body.Plan.Joints[joint].Anchor.Y, DriveTorque);
    }
}

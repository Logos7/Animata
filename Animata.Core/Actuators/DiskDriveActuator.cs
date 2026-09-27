using System.Numerics;
using Animata.Core.Entities;

namespace Animata.Core.Actuators;

/// <summary>
/// Napęd różnicowy na płaszczyźnie XY.
/// Turn ∈ [-1, 1] — ułamek maksymalnej prędkości obrotu (dodatni = w lewo, CCW).
/// Step ∈ [0, 1] — ułamek maksymalnej prędkości jazdy do przodu.
/// </summary>
public sealed class DiskDriveActuator : Actuator
{
    public const string TurnPort = "Turn";
    public const string StepPort = "Step";

    private static readonly string[] Ports = [TurnPort, StepPort];

    public float MaxSpeed { get; set; } = 2;
    public float MaxTurnSpeed { get; set; } = 2.5f;

    public override IReadOnlyList<string> InputPorts => Ports;

    public override void Apply(Entity aOwner, IReadOnlyDictionary<string, float> aCommands, float aDelta)
    {
        if (aDelta <= 0)
            return;

        var turn = aCommands.GetValueOrDefault(TurnPort);
        var step = aCommands.GetValueOrDefault(StepPort);
        if (!float.IsFinite(turn) || !float.IsFinite(step))
            throw new ArgumentOutOfRangeException(nameof(aCommands), "Drive commands must be finite.");

        var angle = Math.Clamp(turn, -1, 1) * MathF.Max(0, MaxTurnSpeed) * aDelta;
        var distance = Math.Clamp(step, 0, 1) * MathF.Max(0, MaxSpeed) * aDelta;

        var heading = Vector3.Transform(Vector3.UnitX, aOwner.Body.Rotation);
        var yaw = MathF.Atan2(heading.Y, heading.X) + angle;
        aOwner.Body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw);
        aOwner.Body.Position += new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0) * distance;
    }
}

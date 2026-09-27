using System.Numerics;
using Animata.Core.Entities;

namespace Animata.Core.Actuators;

/// <summary>
/// Napęd z kierownicą (model rowerowy): tylna oś pcha, przednie koła skręcają.
/// Steer ∈ [-1, 1] — ułamek maksymalnego kąta skrętu kół (dodatni = w lewo).
/// Throttle ∈ [-1, 1] — do przodu ułamek MaxSpeed, do tyłu ułamek MaxReverseSpeed.
/// Prędkość obrotu = v / WheelBase · tan(kąt kół), więc bez jazdy nie ma skrętu,
/// a najmniejszy promień zawracania to WheelBase / tan(MaxSteerAngle).
/// </summary>
public sealed class SteeringDriveActuator : Actuator
{
    public const string SteerPort = "Steer";
    public const string ThrottlePort = "Throttle";

    private static readonly string[] Ports = [SteerPort, ThrottlePort];

    public float MaxSpeed { get; set; } = 2.5f;
    public float MaxReverseSpeed { get; set; } = 1;

    /// <summary>Maksymalny kąt skrętu kół w radianach.</summary>
    public float MaxSteerAngle { get; set; } = 35 * MathF.PI / 180;

    public float WheelBase { get; set; } = 0.8f;

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
        var distance = speed * aDelta;

        var heading = Vector3.Transform(Vector3.UnitX, aOwner.Body.Rotation);
        var yaw = MathF.Atan2(heading.Y, heading.X);
        var yawChange = WheelBase > 0 ? distance / WheelBase * MathF.Tan(SteerAngle) : 0;
        var travelYaw = yaw + yawChange / 2;

        aOwner.Body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw + yawChange);
        aOwner.Body.Position += new Vector3(MathF.Cos(travelYaw), MathF.Sin(travelYaw), 0) * distance;
    }
}

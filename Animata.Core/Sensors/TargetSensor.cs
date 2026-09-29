using System.Numerics;
using Animata.Core.Entities;
using Animata.Core.Worlds;

namespace Animata.Core.Sensors;

/// <summary>
/// Namierza jedną encję po Id.
/// Found — 1 gdy cel istnieje w świecie, inaczej 0 (pozostałe porty są wtedy zerami).
/// Distance — odległość środek–środek.
/// Gap — odległość powierzchnia–powierzchnia (BoundingRadius obu encji); ujemna przy nakładaniu.
/// DirectionX/Y/Z — jednostkowy kierunek do celu w układzie lokalnym właściciela (X = przód, Y = lewo).
/// </summary>
public sealed class TargetSensor : Sensor
{
    public const string FoundPort = "Found";
    public const string DistancePort = "Distance";
    public const string GapPort = "Gap";
    public const string DirectionXPort = "DirectionX";
    public const string DirectionYPort = "DirectionY";
    public const string DirectionZPort = "DirectionZ";

    private static readonly string[] Ports =
        [FoundPort, DistancePort, GapPort, DirectionXPort, DirectionYPort, DirectionZPort];

    /// <summary>Porty, z których sterowniki jazdy do celu (CPG, chód, heurystyka walca, sieci stworów) liczą kurs i dojazd.</summary>
    public static readonly IReadOnlyList<string> SteeringPorts = [FoundPort, GapPort, DirectionXPort, DirectionYPort];

    private readonly Dictionary<string, float> _readings = Ports.ToDictionary(aPort => aPort, _ => 0f);

    [Setting]
    public Guid? TargetId { get; set; }

    public override IReadOnlyList<string> OutputPorts => Ports;

    public override IReadOnlyDictionary<string, float> Read(Entity aOwner, World aWorld)
    {
        var target = TargetId is { } id ? aWorld.Find(id) : null;
        if (target is null || ReferenceEquals(target, aOwner))
        {
            foreach (var port in Ports)
                _readings[port] = 0;
            return _readings;
        }

        var displacement = target.Body.Position - aOwner.Body.Position;
        var distance = displacement.Length();
        var localDirection = distance > 0
            ? Vector3.Transform(displacement / distance, Quaternion.Inverse(aOwner.Body.Rotation))
            : Vector3.Zero;

        _readings[FoundPort] = 1;
        _readings[DistancePort] = distance;
        _readings[GapPort] = distance - aOwner.BoundingRadius - target.BoundingRadius;
        _readings[DirectionXPort] = localDirection.X;
        _readings[DirectionYPort] = localDirection.Y;
        _readings[DirectionZPort] = localDirection.Z;
        return _readings;
    }
}

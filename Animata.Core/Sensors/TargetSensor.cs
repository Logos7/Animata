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

    /// <summary>Porty, z których sterowniki jazdy do celu (CPG, chód, heurystyka walca, sieci stworów) liczą kurs i dojazd.</summary>
    public static readonly IReadOnlyList<string> SteeringPorts = [FoundPort, GapPort, DirectionXPort, DirectionYPort];

    public TargetSensor() => SetPorts([FoundPort, DistancePort, GapPort, DirectionXPort, DirectionYPort, DirectionZPort]);

    [Setting("Cel", Tip = "Na co patrzy oko: kula, inny stwór albo dowolna bryła. Nauka i tak ćwiczy na własnych celach — to zmienia tylko cel w scenie.")]
    public Guid? TargetId { get; set; }

    /// <summary>Kurs do celu z wejść sterownika nazwanych jak porty oka (<see cref="SteeringPorts"/>).</summary>
    public static TargetCourse Course(IReadOnlyDictionary<string, float> aInputs) => new(
        aInputs.GetValueOrDefault(FoundPort) > 0,
        MathF.Atan2(aInputs.GetValueOrDefault(DirectionYPort), aInputs.GetValueOrDefault(DirectionXPort)),
        aInputs.GetValueOrDefault(GapPort));

    public override IReadOnlyDictionary<string, float> Read(Entity aOwner, World aWorld)
    {
        var target = TargetId is { } id ? aWorld.Find(id) : null;
        if (target is null || ReferenceEquals(target, aOwner))
        {
            foreach (var port in OutputPorts)
                Readings[port] = 0;
            return Readings;
        }

        var displacement = target.Body.Position - aOwner.Body.Position;
        var distance = displacement.Length();
        var localDirection = distance > 0
            ? Vector3.Transform(displacement / distance, Quaternion.Inverse(aOwner.Body.Rotation))
            : Vector3.Zero;

        Readings[FoundPort] = 1;
        Readings[DistancePort] = distance;
        Readings[GapPort] = distance - aOwner.BoundingRadius - target.BoundingRadius;
        Readings[DirectionXPort] = localDirection.X;
        Readings[DirectionYPort] = localDirection.Y;
        Readings[DirectionZPort] = localDirection.Z;
        return Readings;
    }
}

/// <summary>
/// Kurs do celu dla sterowników jazdy i chodu: czy cel widać, kąt do niego (rad, w lewo dodatni) i szczelina.
/// </summary>
public readonly record struct TargetCourse(bool Found, float Angle, float Gap)
{
    /// <summary>Kąt do celu jako ułamek π (−1…1); 0, gdy celu nie widać.</summary>
    public float Bearing => Found ? Angle / MathF.PI : 0;

    /// <summary>Napęd słabnący przy celu: szczelina / <paramref name="aArrivalGap"/> przycięta do 0…1; 1, gdy celu nie widać.</summary>
    public float Drive(float aArrivalGap) => Found ? Math.Clamp(Gap / aArrivalGap, 0, 1) : 1;
}

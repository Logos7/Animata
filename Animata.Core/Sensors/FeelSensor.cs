using System.Numerics;
using Animata.Core.Entities;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Core.Sensors;

/// <summary>
/// Czucie terenu i ciała (slot „Feel”) stwora z części — to, czego potrzeba, żeby wejść na próg zamiast się o niego obić:
/// - Ahead: o ile teren <see cref="Reach"/> przed głową jest wyżej niż pod nią (×10, czyli 1 = próg 10 cm; [-1, 1]);
/// - HeadPitch: pochylenie głowy (składowa pionowa kierunku głowy ×2; + = w górę; [-1, 1]);
/// - HeadRoll: przechył głowy na bok (składowa pionowa osi Y głowy ×2; + = lewy bok w górę; [-1, 1]) — równowaga;
/// - Touch: ułamek części, które dotykają czegoś (0 = w powietrzu, 1 = całe ciało na ziemi).
/// Głowa to pierwsza część planu; wysokość terenu z <see cref="Terrain.HeightAt"/> (podłogi i płyty).
/// </summary>
public sealed class FeelSensor : Sensor
{
    public const string AheadPort = "Ahead";
    public const string HeadPitchPort = "HeadPitch";
    public const string TouchPort = "Touch";
    public const string HeadRollPort = "HeadRoll";

    private static readonly string[] Ports = [AheadPort, HeadPitchPort, TouchPort, HeadRollPort];
    private readonly Dictionary<string, float> _readings = Ports.ToDictionary(aPort => aPort, _ => 0f);

    /// <summary>Jak daleko przed głową (m) sprawdzać teren.</summary>
    [Setting]
    public float Reach { get; set; } = 0.3f;

    public override IReadOnlyList<string> OutputPorts => Ports;

    public override IReadOnlyDictionary<string, float> Read(Entity aOwner, World aWorld)
    {
        if (aOwner is not ArticulatedCreature creature || creature.PartPositions.Count == 0)
            return _readings;
        var head = creature.PartPositions[0];
        var forward = Vector3.Transform(Vector3.UnitX, creature.PartOrientations[0]);
        var flat = new Vector2(forward.X, forward.Y);
        flat = flat.LengthSquared() > 1e-6f ? Vector2.Normalize(flat) : Vector2.UnitX;
        var here = Terrain.HeightAt(aWorld, new Vector2(head.X, head.Y));
        var ahead = Terrain.HeightAt(aWorld, new Vector2(head.X, head.Y) + flat * Reach);
        _readings[AheadPort] = Math.Clamp((ahead - here) * 10, -1, 1);
        _readings[HeadPitchPort] = Math.Clamp(forward.Z * 2, -1, 1);
        _readings[HeadRollPort] = Math.Clamp(Vector3.Transform(Vector3.UnitY, creature.PartOrientations[0]).Z * 2, -1, 1);
        var touching = 0;
        for (var part = 0; part < creature.PartPositions.Count; part++)
            if (creature.IsPartTouching(part))
                touching++;
        _readings[TouchPort] = touching / (float)creature.PartPositions.Count;
        return _readings;
    }
}

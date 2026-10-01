using System.Numerics;
using Animata.Core.Entities;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Core.Sensors;

/// <summary>
/// Błędnik (gniazdo „Balance”) stwora z części — to, czego potrzeba, żeby utrzymać się w pionie:
/// - Pitch: pochylenie korzenia do przodu (+) i do tyłu (−), rad × <see cref="Gain"/>, obcięte do [-1, 1];
/// - Roll: przechył na bok (+ = w prawo, lewy bok w górę), rad × <see cref="Gain"/>, [-1, 1];
/// - PitchRate, RollRate: jak szybko pochylenie i przechył się zmieniają (rad/s × <see cref="RateGain"/>, [-1, 1]);
/// - Height: o ile korzeń jest niżej (−) albo wyżej (+) niż w pozie spoczynkowej, m × 4, [-1, 1] — upadek to −1;
/// - VelX, VelY: prędkość korzenia w poziomie w kierunku patrzenia (X — do przodu, Y — w lewo), m/s / 2, [-1, 1].
/// Szybkości liczy z różnicy między odczytami (zegar świata), więc pierwszy odczyt po wstawieniu daje 0.
/// </summary>
public sealed class BalanceSensor : Sensor
{
    public const string PitchPort = "Pitch";
    public const string RollPort = "Roll";
    public const string PitchRatePort = "PitchRate";
    public const string RollRatePort = "RollRate";
    public const string HeightPort = "Height";
    public const string VelocityXPort = "VelX";
    public const string VelocityYPort = "VelY";

    private static readonly string[] Ports = [PitchPort, RollPort, PitchRatePort, RollRatePort, HeightPort, VelocityXPort, VelocityYPort];
    private readonly Dictionary<string, float> _readings = Ports.ToDictionary(aPort => aPort, _ => 0f);
    private double _lastTime = double.NaN;
    private float _lastPitch;
    private float _lastRoll;
    private Vector3 _lastRoot;

    /// <summary>Wzmocnienie kątów: 1 na wyjściu = tyle radianów odwrotności (domyślnie 2 → ±0.5 rad = ±29°).</summary>
    [Setting("Czułość kąta", Min = 0.5, Max = 10, Tip = "Pochylenie w radianach razy tyle daje odczyt; 1 = skraj.")]
    public float Gain { get; set; } = 2;

    /// <summary>Wzmocnienie szybkości (domyślnie 0.5 → ±2 rad/s).</summary>
    [Setting("Czułość szybkości", Min = 0.05, Max = 5, Tip = "Szybkość pochylania (rad/s) razy tyle daje odczyt; 1 = skraj.")]
    public float RateGain { get; set; } = 0.5f;

    public override IReadOnlyList<string> OutputPorts => Ports;

    public override IReadOnlyDictionary<string, float> Read(Entity aOwner, World aWorld)
    {
        if (aOwner is not ArticulatedCreature creature || creature.PartOrientations.Count == 0)
            return _readings;
        var (pitch, roll) = Angles(creature.PartOrientations[0]);
        var dt = double.IsNaN(_lastTime) ? 0 : aWorld.Time - _lastTime;
        var pitchRate = dt > 1e-6 ? (float)((pitch - _lastPitch) / dt) : 0;
        var rollRate = dt > 1e-6 ? (float)((roll - _lastRoll) / dt) : 0;
        var root = creature.PartPositions[0];
        var velocity = dt > 1e-6 ? (root - _lastRoot) / (float)dt : Vector3.Zero;
        _lastTime = aWorld.Time;
        _lastPitch = pitch;
        _lastRoll = roll;
        _lastRoot = root;
        var forward = Vector3.Transform(Vector3.UnitX, creature.PartOrientations[0]);
        var heading = new Vector2(forward.X, forward.Y);
        heading = heading.LengthSquared() > 1e-6f ? Vector2.Normalize(heading) : Vector2.UnitX;
        _readings[VelocityXPort] = Math.Clamp((velocity.X * heading.X + velocity.Y * heading.Y) / 2, -1, 1);
        _readings[VelocityYPort] = Math.Clamp((-velocity.X * heading.Y + velocity.Y * heading.X) / 2, -1, 1);

        var ground = Terrain.HeightAt(aWorld, new Vector2(root.X, root.Y));
        var rest = creature.Plan.Root.Position.Z;
        _readings[PitchPort] = Math.Clamp(pitch * Gain, -1, 1);
        _readings[RollPort] = Math.Clamp(roll * Gain, -1, 1);
        _readings[PitchRatePort] = Math.Clamp(pitchRate * RateGain, -1, 1);
        _readings[RollRatePort] = Math.Clamp(rollRate * RateGain, -1, 1);
        _readings[HeightPort] = Math.Clamp((root.Z - ground - rest) * 4, -1, 1);
        return _readings;
    }

    /// <summary>
    /// Pochylenie i przechył orientacji (rad): pochylenie dodatnie, gdy przód (oś X) opada — ciało pada do przodu;
    /// przechył dodatni, gdy lewy bok (oś Y) idzie w górę — ciało pada w prawo.
    /// </summary>
    public static (float Pitch, float Roll) Angles(Quaternion aOrientation)
    {
        var forward = Vector3.Transform(Vector3.UnitX, aOrientation);
        var left = Vector3.Transform(Vector3.UnitY, aOrientation);
        return (MathF.Asin(Math.Clamp(-forward.Z, -1, 1)), MathF.Asin(Math.Clamp(left.Z, -1, 1)));
    }
}

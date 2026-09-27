using System.Numerics;
using Animata.Core.Entities;
using Animata.Core.Worlds;

namespace Animata.Core.Sensors;

/// <summary>
/// Wąsy: promienie w płaszczyźnie XY pod stałymi kątami względem przodu ciała (dodatni kąt = w lewo).
/// Każdy promień startuje z obrysu właściciela (BoundingRadius) i sięga na Range.
/// Port "Ray{i}" = bliskość ∈ [0, 1]: 0 — nic w zasięgu, 1 — przeszkoda tuż przy ciele.
/// Widzi encje z kategorii <see cref="Detects"/> (domyślnie przeszkody i stwory, bez celów),
/// traktując je jak okręgi o promieniu BoundingRadius.
/// </summary>
public sealed class RaySensor : Sensor
{
    private readonly float[] _angles;
    private readonly string[] _ports;
    private readonly float[] _distances;
    private readonly Dictionary<string, float> _readings = [];

    public RaySensor(IEnumerable<float> aAngles, float aRange = 3)
    {
        _angles = aAngles.ToArray();
        if (_angles.Length == 0 || _angles.Any(aAngle => !float.IsFinite(aAngle)))
            throw new ArgumentException("A ray sensor needs at least one finite angle.", nameof(aAngles));
        _ports = Enumerable.Range(0, _angles.Length).Select(PortName).ToArray();
        _distances = new float[_angles.Length];
        Range = aRange;
        Array.Fill(_distances, aRange);
        foreach (var port in _ports)
            _readings[port] = 0;
    }

    /// <summary>Wachlarz <paramref name="aCount"/> promieni równomiernie od −spread/2 do +spread/2 (rad).</summary>
    public static RaySensor Fan(int aCount, float aSpread, float aRange = 3) =>
        new(aCount == 1
            ? [0f]
            : Enumerable.Range(0, aCount).Select(aIndex => -aSpread / 2 + aSpread * aIndex / (aCount - 1)), aRange);

    public static string PortName(int aIndex) => $"Ray{aIndex}";

    /// <summary>Kąty promieni w radianach, w układzie ciała.</summary>
    public IReadOnlyList<float> Angles => _angles;

    public float Range { get; set; }

    public EntityCategory Detects { get; set; } = EntityCategory.Obstacle | EntityCategory.Creature;

    /// <summary>Odległości trafień od obrysu z ostatniego odczytu (Range = brak trafienia) — do rysowania.</summary>
    public IReadOnlyList<float> LastDistances => _distances;

    public override IReadOnlyList<string> OutputPorts => _ports;

    public override IReadOnlyDictionary<string, float> Read(Entity aOwner, World aWorld)
    {
        var range = MathF.Max(1e-3f, Range);
        var heading = Vector3.Transform(Vector3.UnitX, aOwner.Body.Rotation);
        var yaw = MathF.Atan2(heading.Y, heading.X);
        var center = new Vector2(aOwner.Body.Position.X, aOwner.Body.Position.Y);
        var skin = aOwner.BoundingRadius;

        for (var ray = 0; ray < _angles.Length; ray++)
        {
            var angle = yaw + _angles[ray];
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var origin = center + direction * skin;
            var nearest = range;

            foreach (var entity in aWorld.Entities)
            {
                if (ReferenceEquals(entity, aOwner) || (entity.Category & Detects) == 0)
                    continue;
                var radius = entity.BoundingRadius;
                if (radius <= 0)
                    continue;
                var hit = Intersect(origin, direction, new Vector2(entity.Body.Position.X, entity.Body.Position.Y), radius);
                if (hit < nearest)
                    nearest = hit;
            }

            _distances[ray] = nearest;
            _readings[_ports[ray]] = 1 - nearest / range;
        }
        return _readings;
    }

    /// <summary>Odległość wzdłuż promienia do okręgu; 0 gdy start jest w środku; +∞ gdy pudło.</summary>
    private static float Intersect(Vector2 aOrigin, Vector2 aDirection, Vector2 aCenter, float aRadius)
    {
        var offset = aOrigin - aCenter;
        var c = offset.LengthSquared() - aRadius * aRadius;
        if (c <= 0)
            return 0;
        var b = Vector2.Dot(offset, aDirection);
        if (b >= 0)
            return float.PositiveInfinity;
        var discriminant = b * b - c;
        return discriminant < 0 ? float.PositiveInfinity : -b - MathF.Sqrt(discriminant);
    }
}

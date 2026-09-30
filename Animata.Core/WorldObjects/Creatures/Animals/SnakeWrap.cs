using System.Collections.Concurrent;
using System.Numerics;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Owijanie węża wokół pnia: stałe zgięcie każdego stawu (skręt y, pochylenie p) daje helisę — ten sam ruch śrubowy
/// z segmentu na segment. Dobieramy (y, p) tak, żeby promień helisy był nieco mniejszy niż pień + grubość węża
/// (zwój ściska pień — z tego jest tarcie, które trzyma ciężar), a wznios na segment mały. Wąż staje owinięty,
/// głową w górę; toczenie zwoju (obracanie kierunku zgięcia w czasie) wkręca go w górę pnia.
/// </summary>
public static class SnakeWrap
{
    /// <summary>O ile zwój jest ciaśniejszy niż pień + promień węża (m).</summary>
    public const float Squeeze = 0.03f;

    /// <summary>Wznios helisy na segment (m).</summary>
    public const float Rise = 0.03f;

    private static readonly ConcurrentDictionary<int, (float Yaw, float Pitch)> Cache = new();

    /// <summary>Zgięcie stawu (rad) dla helisy o promieniu (do osi węża) <paramref name="aRadius"/>.</summary>
    public static (float Yaw, float Pitch) BendFor(float aRadius) =>
        Cache.GetOrAdd((int)MathF.Round(aRadius * 1000), aKey =>
        {
            var want = aKey / 1000f;
            var best = (Yaw: 0f, Pitch: 0f, Error: float.MaxValue);
            for (var yaw = 0.2f; yaw <= SnakeCreature.MaxYaw; yaw += 0.01f)
                for (var pitch = -0.5f; pitch <= 0.5f; pitch += 0.01f)
                {
                    var (radius, rise) = Screw(yaw, pitch);
                    var error = MathF.Abs(radius - want) + 2 * MathF.Abs(MathF.Abs(rise) - Rise);
                    if (error < best.Error)
                        best = (yaw, pitch, error);
                }
            return (best.Yaw, best.Pitch);
        });

    /// <summary>Promień i wznios (na segment) helisy ze stałego zgięcia stawów węża.</summary>
    public static (float Radius, float Rise) Screw(float aYaw, float aPitch)
    {
        var (rotation, step) = Step(aYaw, aPitch);
        var angle = 2 * MathF.Acos(Math.Clamp(rotation.W, -1, 1));
        if (angle < 1e-4f)
            return (float.PositiveInfinity, step.Length());
        var axis = Vector3.Normalize(new Vector3(rotation.X, rotation.Y, rotation.Z));
        var along = Vector3.Dot(step, axis);
        var across = (step - along * axis).Length();
        return (across / (2 * MathF.Sin(angle / 2)), along);
    }

    /// <summary>Ruch z segmentu na segment w układzie segmentu: obrót M i przesunięcie środka.</summary>
    private static (Quaternion Rotation, Vector3 Step) Step(float aYaw, float aPitch)
    {
        var bend = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, aYaw) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, aPitch);
        var half = new Vector3(-SnakeCreature.SegmentSpacing / 2, 0, 0);
        return (bend, half + Vector3.Transform(half, bend));
    }

    /// <summary>
    /// Stawia węża owiniętego wokół pionowego pnia (środek podstawy <paramref name="aTrunk"/>, promień <paramref name="aTrunkRadius"/>):
    /// głowa najwyżej, najniższy segment <paramref name="aClearance"/> nad podstawą, <paramref name="aAngle"/> obraca zwój wokół pnia.
    /// </summary>
    public static void Around(SnakeCreature aSnake, Vector3 aTrunk, float aTrunkRadius, float aAngle = 0, float aClearance = 0.12f)
    {
        var (yaw, pitch) = BendFor(aTrunkRadius + SnakeCreature.SegmentRadius - Squeeze);
        var (rotation, step) = Step(yaw, pitch);
        var axis = Vector3.Normalize(new Vector3(rotation.X, rotation.Y, rotation.Z));
        // Oś helisy skierowana tak, by od głowy do ogona schodzić w dół: głowa na górze.
        if (Vector3.Dot(step, axis) > 0)
            axis = -axis;
        var toUp = Align(axis, Vector3.UnitZ);
        var root = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, aAngle) * toUp;

        aSnake.PlaceBent(Vector3.Zero, root, _ => (yaw, pitch));
        var points = aSnake.PartPositions.Select(aPoint => new Vector2(aPoint.X, aPoint.Y)).ToList();
        var center = CircleCenter(points);
        var lowest = aSnake.PartPositions.Min(aPoint => aPoint.Z);
        var shift = new Vector3(aTrunk.X - center.X, aTrunk.Y - center.Y, aTrunk.Z + aClearance + SnakeCreature.SegmentRadius - lowest);
        aSnake.PlaceBent(shift, root, _ => (yaw, pitch));
    }

    private static Quaternion Align(Vector3 aFrom, Vector3 aTo)
    {
        var cross = Vector3.Cross(aFrom, aTo);
        var dot = Math.Clamp(Vector3.Dot(aFrom, aTo), -1, 1);
        if (cross.Length() < 1e-6f)
            return dot > 0 ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        return Quaternion.CreateFromAxisAngle(Vector3.Normalize(cross), MathF.Acos(dot));
    }

    /// <summary>Środek okręgu najlepiej pasującego do punktów (spadek gradientu od średniej).</summary>
    private static Vector2 CircleCenter(List<Vector2> aPoints)
    {
        var center = aPoints.Aggregate(Vector2.Zero, (aSum, aPoint) => aSum + aPoint) / aPoints.Count;
        for (var iteration = 0; iteration < 300; iteration++)
        {
            var radius = aPoints.Average(aPoint => Vector2.Distance(aPoint, center));
            var gradient = Vector2.Zero;
            foreach (var point in aPoints)
            {
                var distance = MathF.Max(Vector2.Distance(point, center), 1e-4f);
                gradient += (distance - radius) * (center - point) / distance;
            }
            center -= gradient / aPoints.Count * 0.5f;
        }
        return center;
    }
}

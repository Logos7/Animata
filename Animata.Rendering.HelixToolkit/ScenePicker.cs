using System.Numerics;
using Avalonia;
using Animata.Core.Bodies;
using Animata.Core.Entities;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;
using HelixToolkit.Avalonia.SharpDX;

namespace Animata.Rendering.HelixToolkit;

/// <summary>
/// Picking myszą: promień z kamery przez piksel i trafienia w uproszczone bryły encji
/// (kula celu, pionowe walce: walec, autko, słupek). Obrót ciał jest tylko wokół Z, więc walce zostają pionowe.
/// </summary>
internal static class ScenePicker
{
    /// <summary>Nadwozie autka wisi na kołach: dół na wysokości promienia koła (hitbox autka to walec od ziemi).</summary>
    private const float CarClearance = 0.13f;

    /// <summary>
    /// Promień przez punkt ekranu. Układ prawoskrętny (LookAtRH): prawo = przód × góra, jak w FlyCameraController —
    /// odwrotna kolejność (góra × przód) daje lustro w poziomie i przeciąganie po skosie „obraca się” o 90°.
    /// FieldOfView kamery Helixa to pionowy kąt w stopniach.
    /// </summary>
    public static bool TryRay(PerspectiveCamera aCamera, Size aViewport, Point aPoint, out Vector3 aOrigin, out Vector3 aDirection)
    {
        aOrigin = aCamera.Position;
        aDirection = default;
        var width = aViewport.Width;
        var height = aViewport.Height;
        if (width <= 0 || height <= 0)
            return false;

        var forward = Vector3.Normalize(aCamera.LookDirection);
        var right = Vector3.Normalize(Vector3.Cross(forward, aCamera.UpDirection));
        var up = Vector3.Cross(right, forward);
        var halfTan = MathF.Tan((float)(aCamera.FieldOfView * Math.PI / 180) / 2);
        var x = (float)(2 * aPoint.X / width - 1) * (float)(width / height) * halfTan;
        var y = (float)(1 - 2 * aPoint.Y / height) * halfTan;
        aDirection = Vector3.Normalize(forward + right * x + up * y);
        return true;
    }

    /// <summary>
    /// Rzut punktu świata na ekran (odwrotność <see cref="TryRay"/>). False, gdy punkt jest za kamerą.
    /// </summary>
    public static bool TryProject(PerspectiveCamera aCamera, Size aViewport, Vector3 aWorld, out Point aPoint)
    {
        aPoint = default;
        var width = aViewport.Width;
        var height = aViewport.Height;
        if (width <= 0 || height <= 0)
            return false;

        var forward = Vector3.Normalize(aCamera.LookDirection);
        var right = Vector3.Normalize(Vector3.Cross(forward, aCamera.UpDirection));
        var up = Vector3.Cross(right, forward);
        var offset = aWorld - aCamera.Position;
        var depth = Vector3.Dot(offset, forward);
        if (depth <= 1e-4f)
            return false;

        var halfTan = MathF.Tan((float)(aCamera.FieldOfView * Math.PI / 180) / 2);
        var x = Vector3.Dot(offset, right) / depth / ((float)(width / height) * halfTan);
        var y = Vector3.Dot(offset, up) / depth / halfTan;
        aPoint = new Point((x + 1) / 2 * width, (1 - y) / 2 * height);
        return true;
    }

    /// <summary>Punkt, w którym promień przecina poziomą płaszczyznę z = <paramref name="aHeight"/> przed kamerą.</summary>
    public static bool TryGroundPoint(Vector3 aOrigin, Vector3 aDirection, float aHeight, out Vector3 aResult)
    {
        aResult = default;
        if (MathF.Abs(aDirection.Z) < 0.0001f)
            return false;
        var distance = (aHeight - aOrigin.Z) / aDirection.Z;
        if (distance <= 0)
            return false;
        aResult = aOrigin + aDirection * distance;
        aResult.Z = aHeight;
        return true;
    }

    /// <summary>Najbliższa trafiona encja albo null.</summary>
    public static Entity? Pick(World aWorld, Vector3 aOrigin, Vector3 aDirection)
    {
        Entity? nearest = null;
        var best = float.PositiveInfinity;
        foreach (var entity in aWorld.Entities)
        {
            var hit = entity switch
            {
                TargetBall target => HitBall(target, aOrigin, aDirection),
                CylinderCreature cylinder => HitCylinder(cylinder.Body, cylinder.Radius, cylinder.Height, aOrigin, aDirection),
                CarCreature car => HitCylinder(car.Body, 0.5f * MathF.Max(car.Length, car.Width),
                    CarClearance + car.Height, aOrigin, aDirection),
                Obstacle obstacle => HitCylinder(obstacle.Body, obstacle.Radius, obstacle.Height, aOrigin, aDirection),
                Slab slab => HitSlab(slab, aOrigin, aDirection),
                Tree tree => HitCylinder(tree.Body, tree.Radius, tree.Height, aOrigin, aDirection),
                ArticulatedCreature body => HitParts(body, aOrigin, aDirection),
                // Podłoga (i każda encja IsFixed) nie jest łapana — kliknięcie w nią nic nie zaznacza ani nie przesuwa.
                _ => null
            };
            if (hit is float distance && distance < best)
            {
                nearest = entity;
                best = distance;
            }
        }
        return nearest;
    }

    /// <summary>Części stwora jako kule (kapsuła — kula obejmująca całą kapsułę).</summary>
    private static float? HitParts(ArticulatedCreature aCreature, Vector3 aOrigin, Vector3 aDirection)
    {
        float? best = null;
        for (var index = 0; index < aCreature.PartPositions.Count; index++)
        {
            var part = aCreature.Plan.Parts[index];
            var radius = part.Shape == PartShape.Capsule ? part.Size.X + part.Size.Y / 2 : part.Radius;
            if (HitSphere(aCreature.PartPositions[index], radius, aOrigin, aDirection) is float distance && (best is null || distance < best))
                best = distance;
        }
        return best;
    }

    private static float? HitSphere(Vector3 aCenter, float aRadius, Vector3 aOrigin, Vector3 aDirection)
    {
        var offset = aOrigin - aCenter;
        var projection = Vector3.Dot(offset, aDirection);
        var discriminant = projection * projection - (offset.LengthSquared() - aRadius * aRadius);
        if (discriminant < 0)
            return null;
        var near = -projection - MathF.Sqrt(discriminant);
        return near > 0 ? near : null;
    }

    /// <summary>Promień kontra płyta: prostopadłościan w jej układzie (środek spodu w Body.Position, obrót wokół pionu).</summary>
    private static float? HitSlab(Slab aSlab, Vector3 aOrigin, Vector3 aDirection)
    {
        var inverse = Quaternion.Inverse(aSlab.Body.Rotation);
        var origin = Vector3.Transform(aOrigin - aSlab.Body.Position, inverse);
        var direction = Vector3.Transform(aDirection, inverse);
        var min = new Vector3(-aSlab.Size.X / 2, -aSlab.Size.Y / 2, 0);
        var max = new Vector3(aSlab.Size.X / 2, aSlab.Size.Y / 2, aSlab.Size.Z);
        var near = float.NegativeInfinity;
        var far = float.PositiveInfinity;
        for (var axis = 0; axis < 3; axis++)
        {
            var o = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
            var d = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
            var lo = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            var hi = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;
            if (MathF.Abs(d) < 1e-8f)
            {
                if (o < lo || o > hi)
                    return null;
                continue;
            }
            var t1 = (lo - o) / d;
            var t2 = (hi - o) / d;
            near = MathF.Max(near, MathF.Min(t1, t2));
            far = MathF.Min(far, MathF.Max(t1, t2));
            if (near > far)
                return null;
        }
        return near > 0 ? near : far > 0 ? far : null;
    }

    /// <summary>Kulka leży na <see cref="Body.Position"/> (środek o promień wyżej).</summary>
    private static float? HitBall(TargetBall aBall, Vector3 aOrigin, Vector3 aDirection) => aBall.Radius <= 0
        ? null
        : HitSphere(aBall.Body.Position + Vector3.UnitZ * aBall.Radius, aBall.Radius, aOrigin, aDirection);

    /// <summary>Promień kontra pionowy walec z denkami stojący na <see cref="Body.Position"/>.</summary>
    private static float? HitCylinder(Body aBody, float aRadius, float aHeight, Vector3 aOrigin, Vector3 aDirection)
    {
        var radius = aRadius;
        var bottom = aBody.Position.Z;
        var top = bottom + aHeight;
        if (radius <= 0 || top <= bottom)
            return null;

        var ox = aOrigin.X - aBody.Position.X;
        var oy = aOrigin.Y - aBody.Position.Y;
        float? best = null;

        void Consider(float aT)
        {
            if (aT > 0 && (best is null || aT < best))
                best = aT;
        }

        var a = aDirection.X * aDirection.X + aDirection.Y * aDirection.Y;
        if (a > 1e-8f)
        {
            var b = ox * aDirection.X + oy * aDirection.Y;
            var c = ox * ox + oy * oy - radius * radius;
            var discriminant = b * b - a * c;
            if (discriminant >= 0)
            {
                var root = MathF.Sqrt(discriminant);
                foreach (var t in new[] { (-b - root) / a, (-b + root) / a })
                {
                    var z = aOrigin.Z + aDirection.Z * t;
                    if (z >= bottom && z <= top)
                        Consider(t);
                }
            }
        }

        if (MathF.Abs(aDirection.Z) > 1e-8f)
            foreach (var capZ in new[] { bottom, top })
            {
                var t = (capZ - aOrigin.Z) / aDirection.Z;
                var x = ox + aDirection.X * t;
                var y = oy + aDirection.Y * t;
                if (x * x + y * y <= radius * radius)
                    Consider(t);
            }

        return best;
    }
}

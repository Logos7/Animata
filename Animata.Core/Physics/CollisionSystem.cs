using System.Numerics;
using Animata.Core.Entities;

namespace Animata.Core.Physics;

/// <summary>
/// Rozpychanie nakładających się encji jako okręgów w płaszczyźnie XY (promień = BoundingRadius).
/// Dwie ruchome dzielą korektę po połowie; ruchoma kontra nieruchoma — ruchoma bierze całość.
/// Kilka iteracji rozwiązuje łańcuchy (A pcha B, B pcha C). O(n²) — wystarczy do kilkuset encji.
/// </summary>
public sealed class CollisionSystem
{
    public bool Enabled { get; set; } = true;
    public int Iterations { get; set; } = 4;

    public void Resolve(IReadOnlyList<Entity> aEntities)
    {
        if (!Enabled)
            return;

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var moved = false;
            for (var i = 0; i < aEntities.Count; i++)
            {
                var a = aEntities[i];
                var radiusA = a.BoundingRadius;
                if (radiusA <= 0)
                    continue;

                for (var j = i + 1; j < aEntities.Count; j++)
                {
                    var b = aEntities[j];
                    var radiusB = b.BoundingRadius;
                    if (radiusB <= 0 || (!a.IsMovable && !b.IsMovable))
                        continue;

                    var delta = new Vector2(b.Body.Position.X - a.Body.Position.X, b.Body.Position.Y - a.Body.Position.Y);
                    var distance = delta.Length();
                    var overlap = radiusA + radiusB - distance;
                    if (overlap <= 0)
                        continue;

                    // Idealnie współśrodkowe: rozsuń w stałym kierunku, żeby wynik był deterministyczny.
                    var normal = distance > 1e-6f ? delta / distance : Vector2.UnitX;
                    var shareA = a.IsMovable ? (b.IsMovable ? 0.5f : 1f) : 0f;
                    var shareB = 1 - shareA;

                    a.Body.Position -= new Vector3(normal * (overlap * shareA), 0);
                    b.Body.Position += new Vector3(normal * (overlap * shareB), 0);
                    moved = true;
                }
            }

            if (!moved)
                return;
        }
    }
}

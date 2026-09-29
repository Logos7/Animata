using System.Numerics;
using Animata.Core.Entities;
using Animata.Core.Worlds;

namespace Animata.Core.WorldObjects;

/// <summary>Wysokość terenu: górne ściany klocków (podłoga to zablokowany klocek).</summary>
public static class Terrain
{
    /// <summary>
    /// Wysokość najwyższej górnej ściany klocka nad punktem (x, y); 0, gdy pod punktem nic nie ma.
    /// <paramref name="aExclude"/> — encja pomijana (np. przesuwany klocek, żeby nie stawał na samym sobie);
    /// <paramref name="aLockedOnly"/> — tylko klocki zablokowane (podłogi).
    /// </summary>
    public static float HeightAt(World aWorld, Vector2 aPoint, Entity? aExclude = null, bool aLockedOnly = false)
    {
        float? best = null;
        foreach (var entity in aWorld.Entities)
            if (entity is Box box && !ReferenceEquals(box, aExclude) && (!aLockedOnly || box.Locked) && box.Covers(aPoint) &&
                (best is null || box.Top > best))
                best = box.Top;
        return best ?? 0;
    }

    /// <summary>
    /// Stawia encję na terenie: Body.Position.Z = wysokość pod jej środkiem. Klocek staje tylko na zablokowanym klocku
    /// (podłodze) — klocki się nie piętrzą, bo dwa nachodzące na siebie podnosiłyby się nawzajem bez końca.
    /// Encji zablokowanych nie rusza. Stwór z części przeniesie się sam (<see cref="ArticulatedCreature.ApplyExternalMove"/>).
    /// Zwraca true, gdy coś się zmieniło.
    /// </summary>
    public static bool Snap(World aWorld, Entity aEntity)
    {
        if (aEntity.Locked)
            return false;
        var position = aEntity.Body.Position;
        var height = HeightAt(aWorld, new Vector2(position.X, position.Y), aEntity, aEntity is Box);
        if (MathF.Abs(position.Z - height) < 1e-5f)
            return false;
        aEntity.Body.Position = position with { Z = height };
        return true;
    }
}

using System.Numerics;
using Animata.Core.Entities;
using Animata.Core.Worlds;

namespace Animata.Core.WorldObjects;

/// <summary>Wysokość terenu: górne ściany podłóg i płyt.</summary>
public static class Terrain
{
    /// <summary>
    /// Wysokość najwyższej powierzchni (podłoga, płyta) nad punktem (x, y); 0, gdy pod punktem nic nie ma.
    /// <paramref name="aExclude"/> — encja pomijana (np. przesuwana płyta, żeby nie stawała na samej sobie);
    /// <paramref name="aFloorsOnly"/> — tylko podłogi.
    /// </summary>
    public static float HeightAt(World aWorld, Vector2 aPoint, Entity? aExclude = null, bool aFloorsOnly = false)
    {
        float? best = null;
        foreach (var entity in aWorld.Entities)
        {
            if (ReferenceEquals(entity, aExclude))
                continue;
            float? top = entity switch
            {
                Floor floor when aPoint.X >= floor.Min.X && aPoint.X <= floor.Max.X && aPoint.Y >= floor.Min.Y && aPoint.Y <= floor.Max.Y =>
                    floor.Top,
                Slab slab when !aFloorsOnly && slab.Covers(aPoint) => slab.Top,
                _ => null
            };
            if (top is { } value && (best is null || value > best))
                best = value;
        }
        return best ?? 0;
    }

    /// <summary>
    /// Stawia encję na terenie: Body.Position.Z = wysokość pod jej środkiem. Płyta staje tylko na podłodze (płyty nie
    /// piętrzą się — dwie nachodzące na siebie podnosiłyby się nawzajem bez końca).
    /// Encji nieruszalnych nie rusza. Stwór z części przeniesie się sam (<see cref="ArticulatedCreature.ApplyExternalMove"/>).
    /// Zwraca true, gdy coś się zmieniło.
    /// </summary>
    public static bool Snap(World aWorld, Entity aEntity)
    {
        if (aEntity.IsFixed)
            return false;
        var position = aEntity.Body.Position;
        var height = HeightAt(aWorld, new Vector2(position.X, position.Y), aEntity, aEntity is Slab);
        if (MathF.Abs(position.Z - height) < 1e-5f)
            return false;
        aEntity.Body.Position = position with { Z = height };
        return true;
    }
}

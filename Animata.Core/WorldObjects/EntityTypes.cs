using Animata.Core.Brains;
using Animata.Core.Entities;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Rodzaj obiektu świata: identyfikator w pliku (<see cref="Id"/>, nie zmieniać — zapisane światy się do niego odwołują),
/// nazwa do UI, ikona (podpowiedź dla Studia) i budowa domyślnego obiektu. Stwór z <see cref="Create"/> jest wyposażony
/// (<see cref="ActiveEntity.Equip"/>) i ma pusty mózg — sam ciało z węzłami zmysłów i napędów; sterownik dokłada się
/// z <see cref="ActiveEntity.BrainPresets"/> albo z pliku.
/// </summary>
public sealed record EntityType(string Id, string Name, string Icon, Type ClrType, Func<Entity> Create)
{
    public bool IsCreature => typeof(ActiveEntity).IsAssignableFrom(ClrType);

    /// <summary>Rodzaj stwora: budowa przez konstruktor (z nowym mózgiem) i <see cref="ActiveEntity.Equip"/>.</summary>
    public static EntityType Creature<T>(string aId, string aName, string aIcon, Func<Brain, T> aCreate) where T : ActiveEntity =>
        new(aId, aName, aIcon, typeof(T), () =>
        {
            var creature = aCreate(new Brain());
            creature.Equip();
            return creature;
        });
}

/// <summary>
/// Rejestr wszystkich rodzajów obiektów świata — jedyne miejsce, w którym trzeba dopisać nowy rodzaj, żeby zapis świata,
/// kopiowanie, menu wstawiania w Studiu i nauka go znały. Kolejność = kolejność w menu.
/// </summary>
public static class EntityTypes
{
    private static readonly List<EntityType> Types =
    [
        EntityType.Creature("car", "Autko", "wheel", aBrain => new CarCreature(aBrain)),
        EntityType.Creature("cylinderCreature", "Walec", "disk", aBrain => new CylinderCreature(aBrain)),
        EntityType.Creature("snake", "Wąż", "snake", aBrain => new SnakeCreature(WorldObjectCatalog.DefaultSnakeSegments, aBrain)),
        EntityType.Creature("spider", "Pająk", "spider", aBrain => new SpiderCreature(aBrain)),
        new("sphere", "Kula", "target", typeof(Sphere), () => new Sphere()),
        new("cylinder", "Cylinder", "pillar", typeof(Cylinder), () => new Cylinder()),
        new("box", "Klocek", "slab", typeof(Box), () => new Box())
    ];

    /// <summary>Wszystkie rodzaje (kopia listy).</summary>
    public static IReadOnlyList<EntityType> All
    {
        get
        {
            lock (Types)
                return [.. Types];
        }
    }

    /// <summary>
    /// Dopisuje rodzaj spoza tej listy (np. stwór z innego projektu albo z testu). Identyfikator i klasa muszą być nowe —
    /// inaczej <see cref="ArgumentException"/>.
    /// </summary>
    public static void Register(EntityType aType)
    {
        lock (Types)
        {
            if (Types.Any(aExisting => aExisting.Id == aType.Id || aExisting.ClrType == aType.ClrType))
                throw new ArgumentException($"Rodzaj „{aType.Id}” ({aType.ClrType.Name}) już jest w rejestrze.", nameof(aType));
            Types.Add(aType);
        }
    }

    /// <summary>Rodzaje stworów (do menu wstawiania).</summary>
    public static IEnumerable<EntityType> Creatures => All.Where(aType => aType.IsCreature);

    /// <summary>Rodzaj po identyfikatorze z pliku albo null.</summary>
    public static EntityType? Find(string aId) => All.FirstOrDefault(aType => aType.Id == aId);

    /// <summary>Rodzaj encji albo null (encja spoza rejestru).</summary>
    public static EntityType? Of(Entity aEntity) => All.FirstOrDefault(aType => aType.ClrType == aEntity.GetType());
}

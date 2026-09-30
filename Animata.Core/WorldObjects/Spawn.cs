using System.Numerics;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Sensors;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Przepis na obiekt świata — jedyna droga, którą obiekt powstaje: sceny i fabryki katalogu, wstawianie w Studiu,
/// plik świata, schowek i rigi nauki składają <see cref="Spawn"/> i wołają <see cref="Build"/>. Kroki zawsze w tej kolejności:
/// <list type="number">
/// <item>rodzaj z rejestru (<see cref="EntityType.Create"/>): ciało, zmysły i napędy, pusty mózg z węzłami ciała;</item>
/// <item><see cref="Settings"/> — ustawienia obiektu (mogą przebudować ciało, np. segmenty węża);</item>
/// <item><see cref="Slots"/> — ustawienia zmysłów i napędów (np. cel oka, kąty wąsów);</item>
/// <item><see cref="Brain"/> — mózg (gotowy, podany sterownik albo zapisany);</item>
/// <item>tożsamość (<see cref="Id"/>, <see cref="Name"/>);</item>
/// <item><see cref="Pose"/> — poza, na końcu, kiedy ciało ma już ostateczny kształt.</item>
/// </list>
/// Nowy rodzaj obiektu nie potrzebuje własnej drogi budowy — wystarczy wpis w <see cref="EntityTypes"/>.
/// </summary>
public sealed record Spawn(EntityType Type)
{
    public Action<Entity>? Settings { get; init; }
    public Action<ActiveEntity>? Slots { get; init; }
    public Action<ActiveEntity>? Brain { get; init; }
    public Guid? Id { get; init; }
    public string? Name { get; init; }
    public Action<Entity>? Pose { get; init; }

    /// <summary>Buduje obiekt według przepisu (kolejność kroków — w opisie klasy).</summary>
    public Entity Build()
    {
        var entity = Type.Create();
        Settings?.Invoke(entity);
        if (entity is ActiveEntity creature)
        {
            Slots?.Invoke(creature);
            Brain?.Invoke(creature);
        }
        if (Id is { } id)
            entity.AssignId(id);
        if (Name is not null)
            entity.Name = Name;
        Pose?.Invoke(entity);
        return entity;
    }

    /// <summary><see cref="Build"/> z rzutowaniem na klasę obiektu.</summary>
    public T Build<T>() where T : Entity => (T)Build();

    // ---------- gotowe kroki ----------

    /// <summary>Ustawienia obiektu klasy <typeparamref name="T"/> (typowane).</summary>
    public static Action<Entity> Configure<T>(Action<T> aSettings) where T : Entity => aEntity => aSettings((T)aEntity);

    /// <summary>Oczy stwora (<see cref="TargetSensor"/>) patrzą na podaną encję (null — bez celu).</summary>
    public static Action<ActiveEntity> Aim(Guid? aTargetId) => aCreature => WorldObjectCatalog.Aim(aCreature, aTargetId);

    /// <summary>Mózg: sensory ciała → podany sterownik → napęd (<see cref="WorldObjectCatalog.BuildBrain"/>).</summary>
    public static Action<ActiveEntity> Controller(BrainModule aController) =>
        aCreature => WorldObjectCatalog.BuildBrain(aCreature.Brain!, aController);

    /// <summary>Mózg z gotowego (<see cref="ActiveEntity.BrainPresets"/>, <see cref="WorldObjectCatalog.InstallBrain"/>).</summary>
    public static Action<ActiveEntity> Preset(Func<ActiveEntity, BrainPreset?> aChoose) => aCreature =>
    {
        if (aChoose(aCreature) is { } preset)
            WorldObjectCatalog.InstallBrain(aCreature, preset);
    };

    /// <summary>Poza: pozycja i kierunek wokół pionu (<see cref="Entity.Place"/>; zablokowany obiekt — wprost w ciało).</summary>
    public static Action<Entity> At(Vector3 aPosition, float aYaw = 0) =>
        At(aPosition, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, aYaw));

    /// <summary>Poza: pozycja i obrót (<see cref="Entity.Place"/>; zablokowany obiekt — wprost w ciało).</summary>
    public static Action<Entity> At(Vector3 aPosition, Quaternion aRotation) => aEntity =>
    {
        if (aEntity.Locked)
        {
            aEntity.Body.Position = aPosition;
            aEntity.Body.Rotation = aRotation;
        }
        else
            aEntity.Place(aPosition, aRotation);
    };

    /// <summary>Łączy kroki tego samego rodzaju (wykonywane po kolei; null pomijany).</summary>
    public static Action<T>? Then<T>(Action<T>? aFirst, Action<T>? aSecond) =>
        aFirst is null ? aSecond : aSecond is null ? aFirst : aValue => { aFirst(aValue); aSecond(aValue); };
}

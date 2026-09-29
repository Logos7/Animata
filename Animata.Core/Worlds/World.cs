using Animata.Core.Entities;
using Animata.Core.Physics;

namespace Animata.Core.Worlds;

/// <summary>
/// Świat. Tick przebiega w fazach, żeby kolejność encji na liście nie miała znaczenia:
/// 1. Think — wszystkie mózgi czytają sensory i liczą komendy (świat jest jeszcze niezmieniony),
/// 2. Act — wszystkie mózgi wysyłają komendy do aktuatorów,
/// 3. Physics — krok Bepu (tylko gdy świat ma fizykę, patrz <see cref="Physics"/>): ruch, kontakty, zderzenia,
/// 4. dodania i usunięcia zlecone w trakcie ticku są stosowane na końcu.
/// Świat z fizyką trzyma pamięć natywną — trzeba go zwolnić (<see cref="Dispose"/>).
/// </summary>
public sealed class World : IDisposable
{
    private readonly List<Entity> _entities = [];
    private readonly Dictionary<Guid, Entity> _index = [];
    private readonly List<Entity> _pendingAdds = [];
    private readonly HashSet<Entity> _pendingRemovals = [];
    private readonly List<IPhysicalEntity> _attached = [];
    private bool _updating;

    public IReadOnlyList<Entity> Entities => _entities;

    /// <summary>
    /// Fizyka brył (Bepu) albo null. Powstaje przy dodaniu pierwszej encji dynamicznej (<see cref="IPhysicalEntity.IsDynamic"/>);
    /// wtedy podpinają się też wszystkie statyczne encje fizyczne (klocki, cylindry).
    /// </summary>
    public PhysicsWorld? Physics { get; private set; }

    /// <summary>
    /// Czas symulacji w sekundach (suma kroków <see cref="Update"/>) — np. zegar rytmu <see cref="Sensors.ClockSensor"/>.
    /// Zapis świata go przechowuje, więc po wczytaniu zegar idzie dalej.
    /// </summary>
    public double Time { get; internal set; }

    public Entity? Find(Guid aId) => _index.GetValueOrDefault(aId);

    public bool Contains(Entity aEntity) =>
        _index.TryGetValue(aEntity.Id, out var existing) && ReferenceEquals(existing, aEntity);

    /// <summary>Dodaje encję. W trakcie ticku dodanie jest odkładane do jego końca.</summary>
    public void Add(Entity aEntity)
    {
        ArgumentNullException.ThrowIfNull(aEntity);
        if (_index.ContainsKey(aEntity.Id) || _pendingAdds.Any(aPending => aPending.Id == aEntity.Id))
            throw new InvalidOperationException($"Entity {aEntity.Id} is already in the world.");

        if (_updating)
            _pendingAdds.Add(aEntity);
        else
            AddNow(aEntity);
    }

    /// <summary>Usuwa encję. W trakcie ticku usunięcie jest odkładane do jego końca.</summary>
    public bool Remove(Entity aEntity)
    {
        if (_pendingAdds.Remove(aEntity))
            return true;
        if (!Contains(aEntity))
            return false;

        if (_updating)
            return _pendingRemovals.Add(aEntity);

        RemoveNow(aEntity);
        return true;
    }

    public void Update(float aDelta)
    {
        if (!float.IsFinite(aDelta) || aDelta < 0)
            throw new ArgumentOutOfRangeException(nameof(aDelta));
        if (_updating)
            throw new InvalidOperationException("World.Update is not reentrant.");

        _updating = true;
        try
        {
            foreach (var entity in _entities)
                if (entity is ActiveEntity { Brain: { } brain } active)
                    brain.Think(active, this, aDelta);

            foreach (var entity in _entities)
                if (entity is ActiveEntity { Brain: { } brain } active)
                    brain.Act(active, this, aDelta);

            if (Physics is { } physics)
            {
                foreach (var entity in _attached)
                    entity.BeforePhysicsStep(physics, aDelta);
                physics.Step(aDelta);
                foreach (var entity in _attached)
                    entity.AfterPhysicsStep(physics);
            }

            Time += aDelta;
        }
        finally
        {
            _updating = false;
            Flush();
        }
    }

    private void Flush()
    {
        foreach (var entity in _pendingRemovals)
            RemoveNow(entity);
        _pendingRemovals.Clear();

        foreach (var entity in _pendingAdds)
            AddNow(entity);
        _pendingAdds.Clear();
    }

    public void Dispose()
    {
        foreach (var entity in _attached)
            if (Physics is { } physics)
                entity.DetachPhysics(physics);
        _attached.Clear();
        Physics?.Dispose();
        Physics = null;
    }

    private void AddNow(Entity aEntity)
    {
        _entities.Add(aEntity);
        _index.Add(aEntity.Id, aEntity);
        if (aEntity is not IPhysicalEntity physical)
            return;
        if (Physics is null && physical.IsDynamic)
        {
            Physics = new PhysicsWorld();
            foreach (var entity in _entities)
                if (entity is IPhysicalEntity other && !ReferenceEquals(other, physical))
                    Attach(other);
        }
        if (Physics is not null)
            Attach(physical);
    }

    private void Attach(IPhysicalEntity aEntity)
    {
        aEntity.AttachPhysics(Physics!);
        _attached.Add(aEntity);
    }

    private void RemoveNow(Entity aEntity)
    {
        _entities.Remove(aEntity);
        _index.Remove(aEntity.Id);
        if (aEntity is IPhysicalEntity physical && _attached.Remove(physical) && Physics is not null)
            physical.DetachPhysics(Physics);
    }
}

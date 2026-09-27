using Animata.Core.Entities;
using Animata.Core.Physics;

namespace Animata.Core.Worlds;

/// <summary>
/// Świat. Tick przebiega w fazach, żeby kolejność encji na liście nie miała znaczenia:
/// 1. Think — wszystkie mózgi czytają sensory i liczą komendy (świat jest jeszcze niezmieniony),
/// 2. Act — wszystkie mózgi wysyłają komendy do aktuatorów,
/// 3. Collisions — rozsunięcie nakładających się encji,
/// 4. Update — procesy ciał,
/// 5. dodania i usunięcia zlecone w trakcie ticku są stosowane na końcu.
/// </summary>
public sealed class World
{
    private readonly List<Entity> _entities = [];
    private readonly Dictionary<Guid, Entity> _index = [];
    private readonly List<Entity> _pendingAdds = [];
    private readonly HashSet<Entity> _pendingRemovals = [];
    private bool _updating;

    public IReadOnlyList<Entity> Entities => _entities;

    public CollisionSystem Collisions { get; } = new();

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

            Collisions.Resolve(_entities);

            foreach (var entity in _entities)
                if (entity is ActiveEntity active)
                    active.Update(aDelta);
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

    private void AddNow(Entity aEntity)
    {
        _entities.Add(aEntity);
        _index.Add(aEntity.Id, aEntity);
    }

    private void RemoveNow(Entity aEntity)
    {
        _entities.Remove(aEntity);
        _index.Remove(aEntity.Id);
    }
}

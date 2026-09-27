using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Worlds;

namespace Animata.Core.Brains;

public class Brain
{
    private readonly List<BrainSnapshot> _snapshots = [];

    public BrainGraph Graph { get; } = new();

    /// <summary>Zapisane snapshoty, od najstarszego.</summary>
    public IReadOnlyList<BrainSnapshot> Snapshots => _snapshots;

    /// <summary>Sensory → logika. Świat nie jest zmieniany.</summary>
    public void Think(ActiveEntity aOwner, World aWorld, float aDelta) =>
        Graph.Think(new BrainContext(aOwner, aWorld, aDelta));

    /// <summary>Komendy → aktuatory.</summary>
    public void Act(ActiveEntity aOwner, World aWorld, float aDelta) =>
        Graph.Act(new BrainContext(aOwner, aWorld, aDelta));

    /// <summary>Czyści stan chwilowy modułów (np. przed nową próbą). Parametrów nie rusza.</summary>
    public void Reset() => Graph.Reset();

    /// <summary>Snapshot jednego modułu.</summary>
    public BrainSnapshot Capture(string aLabel, BrainModule aModule) => Capture(aLabel, [aModule]);

    /// <summary>Snapshot podanych modułów (domyślnie wszystkich, które mają stan). Dodawany zawsze.</summary>
    public BrainSnapshot Capture(string aLabel, IEnumerable<BrainModule>? aModules = null) =>
        Add(new BrainSnapshot(Guid.NewGuid(), aLabel, DateTime.UtcNow, Collect(aModules)));

    /// <summary>
    /// Jak <see cref="Capture(string, IEnumerable{BrainModule}?)"/>, ale nie dodaje nic, jeśli najnowszy snapshot
    /// już zawiera dokładnie ten stan tych modułów (wtedy zwraca null). Chroni przed seriami identycznych snapshotów.
    /// </summary>
    public BrainSnapshot? CaptureIfChanged(string aLabel, IEnumerable<BrainModule>? aModules = null)
    {
        var entries = Collect(aModules);
        if (_snapshots.Count > 0 && Contains(_snapshots[^1], entries))
            return null;
        return Add(new BrainSnapshot(Guid.NewGuid(), aLabel, DateTime.UtcNow, entries));
    }

    /// <summary>
    /// Czy moduły mają dokładnie stan zapisany w snapshocie, czyli czy <see cref="Restore"/> niczego by nie zmienił.
    /// Wpisy modułów, których nie ma już w grafie, są pomijane.
    /// </summary>
    public bool Matches(BrainSnapshot aSnapshot)
    {
        foreach (var entry in aSnapshot.Modules)
        {
            var module = Graph.FindDeep(entry.ModuleId);
            if (module is not null && !entry.State.SameAs(module.CaptureState()))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Przywraca stan modułów ze snapshotu (dopasowanie po Id modułu, także w podgrafach — moduł zgrupowany w podgraf
    /// po zrobieniu snapshotu nadal się przywróci). Moduły, których już nie ma w grafie, są pomijane. Zwraca liczbę przywróconych modułów.
    /// </summary>
    public int Restore(BrainSnapshot aSnapshot)
    {
        var restored = 0;
        foreach (var entry in aSnapshot.Modules)
        {
            var module = Graph.FindDeep(entry.ModuleId);
            if (module is null)
                continue;
            module.RestoreState(entry.State);
            restored++;
        }

        Graph.InvalidateDeep();
        return restored;
    }

    /// <summary>Dodaje snapshot z zewnątrz (np. wczytany z pliku).</summary>
    public void AddSnapshot(BrainSnapshot aSnapshot) => _snapshots.Add(aSnapshot);

    public bool RemoveSnapshot(BrainSnapshot aSnapshot) => _snapshots.Remove(aSnapshot);

    /// <summary>Podmienia snapshot na tym samym miejscu listy (np. przeliczony na nowy kształt sieci). False, gdy go nie było.</summary>
    public bool ReplaceSnapshot(BrainSnapshot aOld, BrainSnapshot aNew)
    {
        var index = _snapshots.IndexOf(aOld);
        if (index < 0)
            return false;
        _snapshots[index] = aNew;
        return true;
    }

    private List<ModuleSnapshot> Collect(IEnumerable<BrainModule>? aModules)
    {
        var entries = new List<ModuleSnapshot>();
        foreach (var module in aModules ?? Graph.Modules)
        {
            if (!Graph.ContainsDeep(module))
                throw new ArgumentException($"{module} is not part of this brain.", nameof(aModules));
            if (module.CaptureState() is { } state)
                entries.Add(new ModuleSnapshot(module.Id, module.Name, state));
        }
        return entries;
    }

    private BrainSnapshot Add(BrainSnapshot aSnapshot)
    {
        _snapshots.Add(aSnapshot);
        return aSnapshot;
    }

    /// <summary>Czy snapshot ma każdy z wpisów z tą samą treścią.</summary>
    private static bool Contains(BrainSnapshot aSnapshot, IReadOnlyList<ModuleSnapshot> aEntries) =>
        aEntries.All(aEntry => aSnapshot.Modules.Any(aExisting =>
            aExisting.ModuleId == aEntry.ModuleId && aExisting.State.SameAs(aEntry.State)));
}
